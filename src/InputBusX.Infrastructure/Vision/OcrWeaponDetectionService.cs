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
///
/// Four preprocessing variants are tried per OCR call; best confidence wins:
///   1. lightOnDark           — Otsu, invert
///   2. darkOnLight           — Otsu, no invert
///   3. lightOnDarkAggressive — Otsu +20, invert
///   4. whiteTextIsolation    — keeps only near-white pixels (R,G,B ≥ WhiteThreshold)
///
/// Frame-diff optimisation:
///   Each frame the raw capture is reduced to a white-binary map and compared with the
///   previous one.  When fewer than FrameDiffThreshold% of pixels changed the region is
///   considered stable and OCR is skipped entirely.
///   When a change IS detected we enter a "scan burst" of ScanBurstLength frames so the
///   stability debounce (StabilityFrames) has enough reads to confirm the new weapon.
///
/// Matching strategy (3 tiers):
///   1. Exact whole-word regex match
///   2. Contains match (keyword anywhere in OCR text)
///   3. Fuzzy match (Levenshtein ≤ floor(keyword.length × FuzzyTolerance))
/// </summary>
public sealed class OcrWeaponDetectionService : IWeaponDetectionService, IDisposable
{
    private readonly ILogger<OcrWeaponDetectionService> _logger;
    private TesseractEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private WeaponProfile? _currentWeapon;

    // ── Stability debounce ─────────────────────────────────────────────────
    private const int StabilityFrames = 3;
    private WeaponProfile? _candidateWeapon;
    private int _candidateCount;

    // ── Frame-diff state ───────────────────────────────────────────────────
    // White-binary of the previous captured frame (one byte per pixel, 0=white-text, 255=background).
    private byte[]? _prevBinaryBytes;

    // After a significant diff, force OCR for this many frames so StabilityFrames can be reached.
    private const int ScanBurstLength = StabilityFrames + 2;
    private int _activeScanFrames;

    // 4 % of white-isolated pixels must change to be considered a weapon switch.
    private const double FrameDiffThreshold = 0.04;

    // Minimum per-channel value for a pixel to be classified as "white HUD text".
    private const int WhiteThreshold = 175;

    // ── OCR config ─────────────────────────────────────────────────────────
    private static readonly (string From, string To)[] CharNormMap =
    [
        ("l", "1"),
        ("I", "1"),
    ];

    private const double FuzzyTolerance = 0.25;

    // Both cases so "Razor", "AK-47", "M4A1" are read correctly.
    // Result is uppercased before keyword matching so case in OCR output doesn't matter.
    private const string CharWhitelist =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -";

    public event Action<WeaponProfile?>? WeaponChanged;
    public WeaponProfile? CurrentWeapon => _currentWeapon;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

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

        try { _engine = CreateEngine(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tesseract engine failed to initialize: {Message}", ex.Message);
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

        _prevBinaryBytes = null;
        _activeScanFrames = 0;
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
            using var captured = CaptureRegion(settings);
            var (text, conf, variant) = RunOcrOnCapture(engine, settings, captured);
            if (text == null) return "[capture returned no text]";

            var filtered   = FilterOcrText(text);
            var normalized = NormalizeText(filtered.ToUpperInvariant());

            return $"RAW ({variant}, conf={conf:F0}):\n{text}\n\nFILTRADO:\n{filtered}\n\nNORMALIZADO:\n{normalized}";
        }
        catch (Exception ex)
        {
            var root = UnwrapException(ex);
            _logger.LogError(ex, "TestCapture failed: {Root}", root.Message);
            return $"[error: {root.Message}]";
        }
        finally
        {
            engine?.Dispose();
        }
    }

    private static Exception UnwrapException(Exception ex)
    {
        int depth = 0;
        while (depth++ < 20 && ex is System.Reflection.TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return ex;
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

    private static string GetBaseDirectory()
    {
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory)) return AppContext.BaseDirectory;
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir)) return exeDir;
        return Directory.GetCurrentDirectory();
    }

    private static TesseractEngine CreateEngine()
    {
        var tessdataPath = Path.Combine(GetBaseDirectory(), "tessdata");
        var trainedData  = Path.Combine(tessdataPath, "eng.traineddata");

        if (!File.Exists(trainedData))
            throw new FileNotFoundException(
                $"Tesseract language data not found at '{trainedData}'. " +
                "The application install appears incomplete — reinstall MatrixX " +
                "or restore the 'tessdata' folder next to the executable.",
                trainedData);

        try
        {
            var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.LstmOnly);
            engine.SetVariable("tessedit_char_whitelist", CharWhitelist);
            engine.SetVariable("load_system_dawg", "false");
            engine.SetVariable("load_freq_dawg", "false");
            return engine;
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Tesseract native libraries (leptonica / tesseract) failed to load. " +
                "Install the Microsoft Visual C++ 2015-2022 x64 Redistributable " +
                "(https://aka.ms/vs/17/release/vc_redist.x64.exe) and restart MatrixX.", ex);
        }
        catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
        {
            throw new InvalidOperationException(
                "Tesseract native libraries failed to initialize. " +
                "Install the Microsoft Visual C++ 2015-2022 x64 Redistributable " +
                "(https://aka.ms/vs/17/release/vc_redist.x64.exe) and restart MatrixX.", ex);
        }
        catch (Exception ex)
        {
            var root = ex;
            while (root is System.Reflection.TargetInvocationException { InnerException: { } inner2 })
                root = inner2;
            if (!ReferenceEquals(root, ex))
                throw new InvalidOperationException($"Tesseract init failed: {root.Message}", root);
            throw;
        }
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
                using var captured = CaptureRegion(settings);
                bool regionChanged = ComputeAndStoreDiff(captured);

                if (regionChanged)
                {
                    // Region changed: reset stability debounce and arm a scan burst
                    // so OCR runs for enough frames to reach StabilityFrames.
                    _candidateWeapon  = null;
                    _candidateCount   = 0;
                    _activeScanFrames = ScanBurstLength;
                    _logger.LogDebug("Frame diff triggered scan burst");
                }

                bool shouldRunOcr = _activeScanFrames > 0 || _currentWeapon == null;

                if (!shouldRunOcr)
                {
                    _logger.LogDebug("HUD region stable — skipping OCR");
                    await Task.Delay(settings.IntervalMs, ct);
                    continue;
                }

                if (_activeScanFrames > 0)
                    _activeScanFrames--;

                var (text, _, _) = RunOcrOnCapture(_engine!, settings, captured);
                if (text != null)
                    MatchWeapon(text, settings);

                await Task.Delay(settings.IntervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                var root = UnwrapException(ex);
                _logger.LogError(root, "OCR detection error: {Message}", root.Message);
                await Task.Delay(1000, ct);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Screen capture
    // ──────────────────────────────────────────────────────────────────────

    private Bitmap CaptureRegion(WeaponDetectionSettings s)
    {
        float dpiScale  = GetDpiScale();
        int screenW     = Math.Max(1, GetSystemMetrics(0)); // SM_CXSCREEN (virtual desktop width)
        int screenH     = Math.Max(1, GetSystemMetrics(1)); // SM_CYSCREEN (virtual desktop height)

        int physW = Math.Clamp((int)(s.CaptureWidth  * dpiScale), 1, screenW);
        int physH = Math.Clamp((int)(s.CaptureHeight * dpiScale), 1, screenH);
        int physX = Math.Clamp((int)(s.CaptureX      * dpiScale), 0, screenW - physW);
        int physY = Math.Clamp((int)(s.CaptureY      * dpiScale), 0, screenH - physH);

    // ──────────────────────────────────────────────────────────────────────
    //  Frame-diff (white-binary comparison)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a white-pixel binary from <paramref name="captured"/>, diffs it against the stored
    /// previous binary, updates the stored copy, and returns whether enough pixels changed.
    ///
    /// Using the white-binary (not raw grayscale) means that background pixels — grass, dirt,
    /// sky — are always mapped to 255 regardless of camera movement, so only actual HUD text
    /// changes register as a diff.
    /// </summary>
    private bool ComputeAndStoreDiff(Bitmap captured)
    {
        var binary = GetWhiteBinaryBytes(captured);

        bool changed;
        if (_prevBinaryBytes == null || _prevBinaryBytes.Length != binary.Length)
        {
            changed = true; // first frame, or capture region was resized
        }
        else
        {
            int diffPixels = 0;
            for (int i = 0; i < binary.Length; i++)
                if (binary[i] != _prevBinaryBytes[i]) diffPixels++;
            double diffRatio = (double)diffPixels / binary.Length;
            changed = diffRatio >= FrameDiffThreshold;
            _logger.LogDebug("Frame diff: {Ratio:P1}", diffRatio);
        }

        _prevBinaryBytes = binary;
        return changed;
    }

    /// <summary>
    /// Returns one byte per pixel at the bitmap's original resolution (no upscaling).
    /// White-ish pixels (R,G,B ≥ <see cref="WhiteThreshold"/>) → 0; everything else → 255.
    /// </summary>
    private static byte[] GetWhiteBinaryBytes(Bitmap bmp)
    {
        var rect     = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data     = bmp.LockBits(rect, SysImaging.ImageLockMode.ReadOnly, SysImaging.PixelFormat.Format32bppArgb);
        int stride   = Math.Abs(data.Stride);
        var rawBytes = new byte[stride * bmp.Height];
        Marshal.Copy(data.Scan0, rawBytes, 0, rawBytes.Length);
        bmp.UnlockBits(data);

        var binary = new byte[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                int i    = y * stride + x * 4;
                byte bVal = rawBytes[i];
                byte gVal = rawBytes[i + 1];
                byte rVal = rawBytes[i + 2];
                binary[y * bmp.Width + x] =
                    (rVal >= WhiteThreshold && gVal >= WhiteThreshold && bVal >= WhiteThreshold)
                    ? (byte)0 : (byte)255;
            }
        return binary;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  OCR — 4 preprocessing variants, best confidence wins
    // ──────────────────────────────────────────────────────────────────────

    private (string? Text, float Confidence, string Variant) RunOcrOnCapture(
        TesseractEngine engine, WeaponDetectionSettings s, Bitmap captured)
    {
        string? debugDir  = null;
        string? timestamp = null;
        if (s.DebugSaveImages)
        {
            debugDir  = Path.Combine(Path.GetTempPath(), "matrixx-ocr-debug");
            Directory.CreateDirectory(debugDir);
            timestamp = DateTime.Now.ToString("HHmmss.fff");
            captured.Save(Path.Combine(debugDir, $"{timestamp}_capture.png"), SysImaging.ImageFormat.Png);
        }

        // Three preprocessing strategies — whichever gives Tesseract the highest confidence wins.
        // Each bitmap is created and disposed inline to prevent leaks if earlier variants throw.
        (bool Invert, bool Aggressive, string Name)[] variantDefs =
        [
            (true,  false, "lightOnDark"),
            (false, false, "darkOnLight"),
            (true,  true,  "lightOnDarkAggressive"),
        ];

        string? bestText    = null;
        float   bestConf    = -1f;
        string  bestVariant = "";

        foreach (var (invert, aggressive, variantName) in variantDefs)
        {
            using var bmp = PreProcess(captured, invert, aggressive);
            try
            {
                var (text, conf) = OcrBitmap(engine, bmp);

                if (debugDir != null && timestamp != null)
                    bmp.Save(
                        Path.Combine(debugDir, $"{timestamp}_{variantName}_conf{conf:F0}.png"),
                        SysImaging.ImageFormat.Png);

                if (conf > bestConf)
                {
                    bestConf    = conf;
                    bestText    = text;
                    bestVariant = variantName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "OCR variant {Variant} failed", variantName);
            }
        }

        _logger.LogDebug("OCR best: {Variant} conf={Conf:F0} text=\"{Text}\"",
            bestVariant, bestConf, bestText);

        return (bestText, bestConf, bestVariant);
    }

    private static (string Text, float Confidence) OcrBitmap(TesseractEngine engine, Bitmap bmp)
    {
        // Save to PNG bytes then load via Pix.LoadFromMemory — avoids BitmapToPixConverter
        // which wraps any native error in TargetInvocationException.
        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, SysImaging.ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        using var pix  = Pix.LoadFromMemory(pngBytes);
        using var page = engine.Process(pix, PageSegMode.SingleLine);
        float  conf = page.GetMeanConfidence() * 100f;
        string text = page.GetText()?.Trim() ?? string.Empty;
        return (text, conf);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Preprocessing
    // ──────────────────────────────────────────────────────────────────────

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
            byte gray   = ToGray(pixelData[i], pixelData[i + 1], pixelData[i + 2]);
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

    /// <summary>
    /// Scale 3× then isolate near-white pixels (R,G,B ≥ <see cref="WhiteThreshold"/>) as black
    /// text on a white background.  Bypasses Otsu entirely, so the complex 3D environment behind
    /// the HUD does not influence the threshold — only the bright white letters matter.
    /// </summary>
    private static Bitmap ExtractWhiteText(Bitmap src)
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

        for (int i = 0; i < bytes; i += 4)
        {
            byte bVal    = pixelData[i];
            byte gVal    = pixelData[i + 1];
            byte rVal    = pixelData[i + 2];
            bool isWhite = rVal >= WhiteThreshold && gVal >= WhiteThreshold && bVal >= WhiteThreshold;
            byte output  = isWhite ? (byte)0 : (byte)255;
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

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
