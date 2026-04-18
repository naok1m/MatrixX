using System.Drawing;
using SysImaging = System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace InputBusX.Infrastructure.Vision;

/// <summary>
/// Uses Tesseract 5 (LSTM, PSM=SingleLine, whitelist, no dictionary) and System.Drawing
/// screen capture to detect the current weapon from a configurable HUD region.
/// Three preprocessing variants are tried per frame; the one with the highest
/// Tesseract mean-confidence wins.
///
/// Matching strategy (3 layers):
///   1. Exact match after normalization          — fastest, most precise
///   2. Contains match (keyword inside OCR text) — handles extra words around the name
///   3. Fuzzy match (Levenshtein ≤ threshold)    — tolerates dropped/swapped characters
/// </summary>
public sealed class OcrWeaponDetectionService : IWeaponDetectionService, IDisposable
{
    private readonly ILogger<OcrWeaponDetectionService> _logger;
    private TesseractEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private WeaponProfile? _currentWeapon;

    // Stability debounce: require this many consecutive matching frames before switching.
    private const int StabilityFrames = 3;
    private WeaponProfile? _candidateWeapon;
    private int _candidateCount;

    public event Action<WeaponProfile?>? WeaponChanged;
    public WeaponProfile? CurrentWeapon => _currentWeapon;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    // Only the safest, unambiguous single-char confusions for HUD fonts.
    private static readonly (string From, string To)[] CharNormMap =
    [
        ("l", "1"),   // lowercase L → 1
        ("I", "1"),   // uppercase I → 1
    ];

    private const double FuzzyTolerance = 0.25;

    // Restrict Tesseract to the characters that can actually appear in weapon names.
    // No dictionary words, no punctuation guessing, no digit↔letter confusion.
    private const string CharWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -";

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

        try
        {
            _engine = CreateEngine();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract engine failed to initialize — check tessdata/eng.traineddata");
            return;
        }

        _logger.LogInformation("Tesseract 5 OCR engine initialized");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => DetectionLoop(settings, _cts.Token), _cts.Token);
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

        if (_loopTask != null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogDebug(ex, "Loop task ended with exception on stop"); }
            _loopTask = null;
        }

        _engine?.Dispose();
        _engine = null;

        _candidateWeapon = null;
        _candidateCount  = 0;
        SetCurrentWeapon(null);
        _logger.LogInformation("Weapon detection stopped");
    }

    /// <summary>Single-shot capture — shows RAW, FILTRADO, NORMALIZADO for debugging.</summary>
    public async Task<string> TestCaptureAsync(WeaponDetectionSettings settings)
    {
        TesseractEngine? engine = null;
        try
        {
            engine = CreateEngine();
            var (text, conf, variant) = RunOcrOnRegion(engine, settings);
            if (text == null) return "[capture returned no text]";

            var filtered   = FilterOcrText(text);
            var normalized = NormalizeText(filtered.ToUpperInvariant());

            return $"RAW ({variant}, conf={conf:F0}):\n{text}\n\nFILTRADO:\n{filtered}\n\nNORMALIZADO:\n{normalized}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestCapture failed");
            return $"[error: {ex.Message}]";
        }
        finally
        {
            engine?.Dispose();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _engine?.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Engine factory
    // ──────────────────────────────────────────────────────────────────────

    private static TesseractEngine CreateEngine()
    {
        var tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", CharWhitelist);
        engine.SetVariable("load_system_dawg", "false");
        engine.SetVariable("load_freq_dawg", "false");
        return engine;
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
                var (text, _, _) = RunOcrOnRegion(_engine!, settings);
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
    //  Screen capture + pre-processing + OCR (3 variants, best confidence wins)
    // ──────────────────────────────────────────────────────────────────────

    private (string? Text, float Confidence, string Variant) RunOcrOnRegion(
        TesseractEngine engine, WeaponDetectionSettings s)
    {
        float dpiScale = GetDpiScale();
        int physX = (int)(s.CaptureX      * dpiScale);
        int physY = (int)(s.CaptureY      * dpiScale);
        int physW = Math.Max(1, (int)(s.CaptureWidth  * dpiScale));
        int physH = Math.Max(1, (int)(s.CaptureHeight * dpiScale));

        using var captured = new Bitmap(physW, physH, SysImaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(captured))
        {
            g.CopyFromScreen(physX, physY, 0, 0,
                new Size(physW, physH),
                CopyPixelOperation.SourceCopy);
        }

        string? debugDir   = null;
        string? timestamp  = null;
        if (s.DebugSaveImages)
        {
            debugDir  = Path.Combine(Path.GetTempPath(), "matrixx-ocr-debug");
            Directory.CreateDirectory(debugDir);
            timestamp = DateTime.Now.ToString("HHmmss.fff");
            captured.Save(Path.Combine(debugDir, $"{timestamp}_capture.png"), SysImaging.ImageFormat.Png);
        }

        // Three preprocessing strategies — whichever gives Tesseract the highest confidence wins.
        Bitmap[] variants =
        [
            PreProcess(captured, invert: true,  aggressiveThreshold: false),
            PreProcess(captured, invert: false, aggressiveThreshold: false),
            PreProcess(captured, invert: true,  aggressiveThreshold: true),
        ];
        string[] variantNames = ["lightOnDark", "darkOnLight", "lightOnDarkAggressive"];

        string? bestText    = null;
        float   bestConf    = -1f;
        string  bestVariant = "";

        for (int vi = 0; vi < variants.Length; vi++)
        {
            using var bmp = variants[vi];
            try
            {
                var (text, conf) = OcrBitmap(engine, bmp);

                if (debugDir != null && timestamp != null)
                    bmp.Save(
                        Path.Combine(debugDir, $"{timestamp}_{variantNames[vi]}_conf{conf:F0}.png"),
                        SysImaging.ImageFormat.Png);

                if (conf > bestConf)
                {
                    bestConf    = conf;
                    bestText    = text;
                    bestVariant = variantNames[vi];
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "OCR variant {Variant} failed", variantNames[vi]);
            }
        }

        _logger.LogDebug("OCR best variant: {Variant} conf={Conf:F0} text=\"{Text}\"",
            bestVariant, bestConf, bestText);

        return (bestText, bestConf, bestVariant);
    }

    private static (string Text, float Confidence) OcrBitmap(TesseractEngine engine, Bitmap bmp)
    {
        var converter = new BitmapToPixConverter();
        using var pix  = converter.Convert(bmp);
        using var page = engine.Process(pix, PageSegMode.SingleLine);
        // GetMeanConfidence returns 0–1; multiply by 100 for percent display
        float conf = page.GetMeanConfidence() * 100f;
        string text = page.GetText()?.Trim() ?? string.Empty;
        return (text, conf);
    }

    /// <summary>
    /// Scale 3× → grayscale → Otsu binarization.
    /// <paramref name="invert"/>: true = light-on-dark HUD (bright text → output as black).
    /// <paramref name="aggressiveThreshold"/>: raises Otsu threshold +20 to capture borderline pixels.
    /// </summary>
    private static Bitmap PreProcess(Bitmap src, bool invert, bool aggressiveThreshold)
    {
        const int scale = 3;
        int dstW = src.Width  * scale;
        int dstH = src.Height * scale;

        var scaled = new Bitmap(dstW, dstH, SysImaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, dstW, dstH);
        }

        var rect    = new Rectangle(0, 0, dstW, dstH);
        var bmpData = scaled.LockBits(rect, SysImaging.ImageLockMode.ReadWrite, SysImaging.PixelFormat.Format32bppArgb);
        int bytes      = Math.Abs(bmpData.Stride) * dstH;
        var pixelData  = new byte[bytes];
        Marshal.Copy(bmpData.Scan0, pixelData, 0, bytes);

        var hist = new int[256];
        for (int i = 0; i < bytes; i += 4)
            hist[ToGray(pixelData[i], pixelData[i + 1], pixelData[i + 2])]++;

        int threshold = ComputeOtsuThreshold(hist, dstW * dstH);
        if (aggressiveThreshold)
            threshold = Math.Min(255, threshold + 20);

        for (int i = 0; i < bytes; i += 4)
        {
            byte gray = ToGray(pixelData[i], pixelData[i + 1], pixelData[i + 2]);
            // invert=true  → bright pixels are text → map gray >= threshold to black (0)
            // invert=false → dark pixels are text  → map gray <  threshold to black (0)
            bool isText = invert ? gray >= threshold : gray < threshold;
            byte output = isText ? (byte)0 : (byte)255;
            pixelData[i]     = output;
            pixelData[i + 1] = output;
            pixelData[i + 2] = output;
            pixelData[i + 3] = 255;
        }

        Marshal.Copy(pixelData, 0, bmpData.Scan0, bytes);
        scaled.UnlockBits(bmpData);
        return scaled;
    }

    private static byte ToGray(byte b, byte gn, byte r) =>
        (byte)(0.299 * r + 0.587 * gn + 0.114 * b);

    /// <summary>Otsu's method — finds threshold that minimizes intra-class variance.</summary>
    private static int ComputeOtsuThreshold(int[] hist, int totalPixels)
    {
        double sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += i * hist[i];

        double sumB    = 0;
        int    wB      = 0;
        double maxVar  = 0;
        int    threshold = 128;

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
                maxVar    = betweenVar;
                threshold = t;
            }
        }

        return threshold;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  DPI
    // ──────────────────────────────────────────────────────────────────────

    private static float GetDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96f : 1f;
        }
        catch { return 1f; }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    // ──────────────────────────────────────────────────────────────────────
    //  Text filtering & normalization
    // ──────────────────────────────────────────────────────────────────────

    private static string FilterOcrText(string raw)
    {
        var tokens = raw.Split(
            ['\n', '\r', '\t', '|', '/', '\\', '(', ')', '[', ']', ':'],
            StringSplitOptions.RemoveEmptyEntries);

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
    /// </summary>
    private static bool KeywordMatches(string normalizedOcr, string kw)
    {
        if (Regex.IsMatch(normalizedOcr, $@"\b{Regex.Escape(kw)}\b"))
            return true;

        if (normalizedOcr.Contains(kw))
            return true;

        int maxDist = Math.Max(1, (int)Math.Floor(kw.Length * FuzzyTolerance));
        return FuzzyContains(normalizedOcr, kw, maxDist);
    }

    private static bool FuzzyContains(string haystack, string kw, int maxDist)
    {
        if (kw.Length == 0) return false;
        if (haystack.Length < kw.Length) return Levenshtein(haystack, kw) <= maxDist;
        if (Levenshtein(haystack, kw) <= maxDist) return true;

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
