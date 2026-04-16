using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace InputBusX.Infrastructure.Vision;

/// <summary>
/// Uses the built-in Windows OCR engine (Windows.Media.Ocr) and System.Drawing screen
/// capture to detect the current weapon from a configurable HUD region.
///
/// Matching strategy (3 layers):
///   1. Exact match after normalization          — fastest, most precise
///   2. Contains match (keyword inside OCR text) — handles extra words around the name
///   3. Fuzzy match (Levenshtein ≤ threshold)    — tolerates dropped/swapped characters
/// </summary>
public sealed class OcrWeaponDetectionService : IWeaponDetectionService, IDisposable
{
    private readonly ILogger<OcrWeaponDetectionService> _logger;
    private OcrEngine? _ocrEngine;
    private CancellationTokenSource? _cts;
    private WeaponProfile? _currentWeapon;

    // Stability debounce: require this many consecutive matching frames before switching.
    // Prevents transient OCR glitches from triggering a profile swap.
    private const int StabilityFrames = 3;
    private WeaponProfile? _candidateWeapon;
    private int _candidateCount;

    public event Action<WeaponProfile?>? WeaponChanged;
    public WeaponProfile? CurrentWeapon => _currentWeapon;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    // ── Character normalization ────────────────────────────────────────────
    // Only the safest, unambiguous single-char confusions for HUD fonts.
    // G→9 removed: it corrupts weapon names like "MCW", "Grau", "Groza".
    // O→0 removed: Warzone weapon names genuinely use capital O.
    private static readonly (string From, string To)[] CharNormMap =
    [
        ("l", "1"),    // lowercase L → 1  (unambiguous in HUD fonts)
        ("I", "1"),    // uppercase I → 1  (common in monospace-style HUD fonts)
    ];

    // ── Fuzzy threshold ───────────────────────────────────────────────────
    // Allow up to this fraction of keyword length as edit distance.
    private const double FuzzyTolerance = 0.25;

    public OcrWeaponDetectionService(ILogger<OcrWeaponDetectionService> logger)
    {
        _logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────────

    public async Task StartAsync(WeaponDetectionSettings settings, CancellationToken ct = default)
    {
        await StopAsync();

        // Prefer English — consistent results regardless of OS locale.
        // Falls back to the user's profile language if English is not installed.
        _ocrEngine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages();

        if (_ocrEngine == null)
        {
            _logger.LogWarning("Windows OCR engine unavailable — English language pack may be missing");
            return;
        }

        _logger.LogInformation("OCR engine initialized with language: {Lang}", _ocrEngine.RecognizerLanguage.LanguageTag);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => DetectionLoop(settings, _cts.Token), _cts.Token);
        _logger.LogInformation("Weapon detection started (interval {Interval} ms)", settings.IntervalMs);
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _candidateWeapon = null;
        _candidateCount  = 0;
        SetCurrentWeapon(null);
        _logger.LogInformation("Weapon detection stopped");
        await Task.CompletedTask;
    }

    /// <summary>Single-shot capture — shows RAW, FILTRADO, NORMALIZADO for debugging.</summary>
    public async Task<string> TestCaptureAsync(WeaponDetectionSettings settings)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
            return "[OCR engine not available — check Windows language settings]";

        try
        {
            var raw = await RunOcrOnRegionAsync(engine, settings);
            if (raw == null) return "[capture returned no text]";

            var filtered   = FilterOcrText(raw);
            var normalized = NormalizeText(filtered.ToUpperInvariant());

            return $"RAW:         {raw}\n\nFILTRADO:    {filtered}\n\nNORMALIZADO: {normalized}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestCapture failed");
            return $"[error: {ex.Message}]";
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Detection loop
    // ──────────────────────────────────────────────────────────────────────

    private async Task DetectionLoop(WeaponDetectionSettings settings, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var text = await RunOcrOnRegionAsync(_ocrEngine!, settings);
                if (text != null)
                    MatchWeapon(text, settings);

                await Task.Delay(settings.IntervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR detection error");
                await Task.Delay(1000, ct);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Screen capture + pre-processing + OCR
    // ──────────────────────────────────────────────────────────────────────

    private static async Task<string?> RunOcrOnRegionAsync(OcrEngine engine, WeaponDetectionSettings s)
    {
        // Get DPI scale so we capture physical pixels, not logical points.
        // On a 4K monitor at 150% scaling, a 200x30 logical region = 300x45 physical pixels.
        float dpiScale = GetDpiScale();
        int physX = (int)(s.CaptureX * dpiScale);
        int physY = (int)(s.CaptureY * dpiScale);
        int physW = (int)(s.CaptureWidth  * dpiScale);
        int physH = (int)(s.CaptureHeight * dpiScale);

        // Ensure minimum viable size after DPI correction
        if (physW < 1) physW = 1;
        if (physH < 1) physH = 1;

        using var captured = new Bitmap(physW, physH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(captured))
        {
            // Use physical pixel coordinates for the capture
            g.CopyFromScreen(physX, physY, 0, 0,
                new Size(physW, physH),
                CopyPixelOperation.SourceCopy);
        }

        using var processed = PreProcess(captured);

        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            processed.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        using var ras = new InMemoryRandomAccessStream();
        using (var dw = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            dw.WriteBytes(pngBytes);
            await dw.StoreAsync();
        }

        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var rawBmp = await decoder.GetSoftwareBitmapAsync();
        SoftwareBitmap softBmp;
        if (rawBmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            rawBmp.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
        {
            softBmp = SoftwareBitmap.Convert(rawBmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        else
        {
            softBmp = rawBmp;
        }

        var result = await engine.RecognizeAsync(softBmp);
        if (!ReferenceEquals(softBmp, rawBmp))
            softBmp.Dispose();

        return result.Text;
    }

    /// <summary>
    /// Returns the DPI scale factor of the primary monitor (1.0 at 96dpi/100%, 1.5 at 144dpi/150%).
    /// Uses GetDpiForSystem (Win10+) so it never triggers DPI virtualization.
    /// </summary>
    private static float GetDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96f : 1f;
        }
        catch
        {
            return 1f;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>
    /// Scale 3× then convert to grayscale and apply Otsu binarization (LockBits).
    /// Binary black-on-white is the most reliable input for Windows OCR on HUD text.
    /// </summary>
    private static Bitmap PreProcess(Bitmap src)
    {
        const int scale = 3;
        int dstW = src.Width  * scale;
        int dstH = src.Height * scale;

        // Step 1: Scale up with bicubic interpolation
        var scaled = new Bitmap(dstW, dstH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, dstW, dstH);
        }

        // Step 2: Convert to grayscale and compute Otsu threshold via LockBits
        var rect = new Rectangle(0, 0, dstW, dstH);
        var bmpData = scaled.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(bmpData.Stride) * dstH;
        var pixelData = new byte[bytes];
        Marshal.Copy(bmpData.Scan0, pixelData, 0, bytes);

        // Build grayscale histogram
        var hist = new int[256];
        for (int i = 0; i < bytes; i += 4)
        {
            byte b = pixelData[i];
            byte gn = pixelData[i + 1];
            byte r  = pixelData[i + 2];
            byte gray = (byte)(0.299 * r + 0.587 * gn + 0.114 * b);
            hist[gray]++;
        }

        int otsuThreshold = ComputeOtsuThreshold(hist, dstW * dstH);

        // Binarize: pixels below threshold → black (text), above → white (background)
        // HUD weapon text is typically bright on dark → invert logic based on mean brightness
        double meanBrightness = 0;
        for (int v = 0; v < 256; v++) meanBrightness += v * hist[v];
        meanBrightness /= (dstW * dstH);

        bool darkText = meanBrightness > 127; // bright background → dark text mode

        for (int i = 0; i < bytes; i += 4)
        {
            byte b = pixelData[i];
            byte gn = pixelData[i + 1];
            byte r  = pixelData[i + 2];
            byte gray = (byte)(0.299 * r + 0.587 * gn + 0.114 * b);

            bool isText = darkText ? gray < otsuThreshold : gray >= otsuThreshold;
            byte output = isText ? (byte)0 : (byte)255;

            pixelData[i]     = output; // B
            pixelData[i + 1] = output; // G
            pixelData[i + 2] = output; // R
            pixelData[i + 3] = 255;    // A
        }

        Marshal.Copy(pixelData, 0, bmpData.Scan0, bytes);
        scaled.UnlockBits(bmpData);

        return scaled;
    }

    /// <summary>Otsu's method — finds threshold that minimizes intra-class variance.</summary>
    private static int ComputeOtsuThreshold(int[] hist, int totalPixels)
    {
        double sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += i * hist[i];

        double sumB = 0;
        int wB = 0;
        double maxVar = 0;
        int threshold = 128;

        for (int t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;

            int wF = totalPixels - wB;
            if (wF == 0) break;

            sumB += t * hist[t];
            double mB = sumB / wB;
            double mF = (sumAll - sumB) / wF;

            double betweenVar = (double)wB * wF * (mB - mF) * (mB - mF);
            if (betweenVar > maxVar)
            {
                maxVar = betweenVar;
                threshold = t;
            }
        }

        return threshold;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Text filtering & normalization
    // ──────────────────────────────────────────────────────────────────────

    private static string FilterOcrText(string raw)
    {
        var tokens = raw.Split(
            new[] { '\n', '\r', '\t', '|', '/', '\\', '(', ')', '[', ']', ':' },
            StringSplitOptions.RemoveEmptyEntries);

        // Keep tokens that contain at least one letter (removes "30", "90", "128")
        var withLetters = tokens
            .SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.Any(char.IsLetter));

        return string.Join(" ", withLetters);
    }

    private static string NormalizeText(string upper)
    {
        foreach (var (from, to) in CharNormMap)
            upper = upper.Replace(from, to);
        return upper;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Matching (exact → contains → fuzzy) + stability debounce
    // ──────────────────────────────────────────────────────────────────────

    private void MatchWeapon(string ocrText, WeaponDetectionSettings settings)
    {
        var filtered   = FilterOcrText(ocrText);
        var normalized = NormalizeText(filtered.ToUpperInvariant());

        _logger.LogDebug("OCR normalized: \"{Text}\"", normalized);

        WeaponProfile? matched = null;
        foreach (var weapon in settings.Weapons)
        {
            foreach (var keyword in weapon.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                var kw = NormalizeText(keyword.Trim().ToUpperInvariant());

                if (KeywordMatches(normalized, kw))
                {
                    matched = weapon;
                    break;
                }
            }
            if (matched != null) break;
        }

        // Stability debounce: only switch after StabilityFrames consecutive same result
        if (matched?.Id == _candidateWeapon?.Id)
        {
            _candidateCount++;
        }
        else
        {
            _candidateWeapon = matched;
            _candidateCount  = 1;
        }

        if (_candidateCount >= StabilityFrames && _currentWeapon?.Id != _candidateWeapon?.Id)
        {
            if (_candidateWeapon != null)
                _logger.LogInformation("Weapon detected: {Name} (stable {N} frames)",
                    _candidateWeapon.Name, StabilityFrames);
            else
                _logger.LogInformation("No weapon detected (stable {N} frames)", StabilityFrames);

            SetCurrentWeapon(_candidateWeapon);
        }
    }

    /// <summary>
    /// Three-tier match:
    ///   1. Exact whole-word regex match
    ///   2. Keyword contained anywhere in the OCR string
    ///   3. Fuzzy: Levenshtein distance ≤ floor(keyword.length × FuzzyTolerance)
    ///      — tested both on the full string and on a sliding window of the same length
    /// </summary>
    private static bool KeywordMatches(string normalizedOcr, string kw)
    {
        // 1. Exact whole-word
        var pattern = $@"\b{Regex.Escape(kw)}\b";
        if (Regex.IsMatch(normalizedOcr, pattern))
            return true;

        // 2. Contains
        if (normalizedOcr.Contains(kw))
            return true;

        // 3. Fuzzy sliding-window
        int maxDist = Math.Max(1, (int)Math.Floor(kw.Length * FuzzyTolerance));
        return FuzzyContains(normalizedOcr, kw, maxDist);
    }

    /// <summary>
    /// Slides a window of kw.Length characters across the haystack and checks
    /// whether any window has Levenshtein distance ≤ maxDist from kw.
    /// </summary>
    private static bool FuzzyContains(string haystack, string kw, int maxDist)
    {
        if (kw.Length == 0) return false;
        if (haystack.Length < kw.Length) return Levenshtein(haystack, kw) <= maxDist;

        // Also try the full string (handles cases where haystack ≈ kw in length)
        if (Levenshtein(haystack, kw) <= maxDist) return true;

        // Sliding window — allow window slightly larger than keyword to absorb insertions
        int windowSize = kw.Length + maxDist;
        for (int i = 0; i <= haystack.Length - kw.Length; i++)
        {
            int end    = Math.Min(i + windowSize, haystack.Length);
            var window = haystack[i..end];
            if (Levenshtein(window, kw) <= maxDist)
                return true;
        }

        return false;
    }

    /// <summary>Standard Levenshtein edit distance (insert / delete / substitute).</summary>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            Array.Copy(curr, prev, curr.Length);
        }

        return prev[b.Length];
    }

    private void SetCurrentWeapon(WeaponProfile? weapon)
    {
        _currentWeapon = weapon;
        WeaponChanged?.Invoke(weapon);
    }
}
