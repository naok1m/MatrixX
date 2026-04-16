using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
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

    public event Action<WeaponProfile?>? WeaponChanged;
    public WeaponProfile? CurrentWeapon => _currentWeapon;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    // ── Character normalization ────────────────────────────────────────────
    // Only the most common single-char OCR confusion for game HUD fonts.
    // S→5 / B→8 / Z→2 removed — too aggressive for weapon names with real letters.
    private static readonly (string From, string To)[] CharNormMap =
    [
        ("G", "9"),   // "Razor g mm" → "Razor 9 mm"  (most common HUD font issue)
        ("O", "0"),   // capital O vs zero
    ];

    // ── Fuzzy threshold ───────────────────────────────────────────────────
    // Allow up to this fraction of keyword length as edit distance.
    // 0.30 = 30% → "MK35 ISR" (8 chars) allows ≤2 errors → matches "MK3 SR"
    private const double FuzzyTolerance = 0.30;

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

        _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (_ocrEngine == null)
        {
            _logger.LogWarning("Windows OCR engine unavailable — language pack may be missing");
            return;
        }

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

        SetCurrentWeapon(null);
        _logger.LogInformation("Weapon detection stopped");
        await Task.CompletedTask;
    }

    /// <summary>Single-shot capture — shows RAW, FILTRADO, NORMALIZADO for debugging.</summary>
    public async Task<string> TestCaptureAsync(WeaponDetectionSettings settings)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
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
        using var captured = new Bitmap(s.CaptureWidth, s.CaptureHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(captured))
        {
            g.CopyFromScreen(s.CaptureX, s.CaptureY, 0, 0,
                new Size(s.CaptureWidth, s.CaptureHeight),
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

    /// <summary>Scale 3× + contrast boost for better OCR on small HUD text.</summary>
    private static Bitmap PreProcess(Bitmap src)
    {
        const int scale = 3;
        var dst = new Bitmap(src.Width * scale, src.Height * scale, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.SmoothingMode     = SmoothingMode.HighQuality;

        float c = 1.6f, b = -0.2f;
        var cm = new ColorMatrix(new[]
        {
            new float[] { c, 0, 0, 0, 0 },
            new float[] { 0, c, 0, 0, 0 },
            new float[] { 0, 0, c, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { b, b, b, 0, 1 }
        });

        using var attr = new ImageAttributes();
        attr.SetColorMatrix(cm);
        g.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height),
            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);

        return dst;
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
    //  Matching (exact → contains → fuzzy)
    // ──────────────────────────────────────────────────────────────────────

    private void MatchWeapon(string ocrText, WeaponDetectionSettings settings)
    {
        var filtered   = FilterOcrText(ocrText);
        var normalized = NormalizeText(filtered.ToUpperInvariant());

        _logger.LogDebug("OCR normalized: \"{Text}\"", normalized);

        foreach (var weapon in settings.Weapons)
        {
            foreach (var keyword in weapon.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;

                var kw = NormalizeText(keyword.Trim().ToUpperInvariant());

                if (KeywordMatches(normalized, kw))
                {
                    if (_currentWeapon?.Id != weapon.Id)
                    {
                        _logger.LogInformation("Weapon detected: {Name} (kw: \"{Kw}\")",
                            weapon.Name, keyword.Trim());
                        SetCurrentWeapon(weapon);
                    }
                    return;
                }
            }
        }

        if (_currentWeapon != null)
            SetCurrentWeapon(null);
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
