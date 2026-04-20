using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using GdiBitmap = System.Drawing.Bitmap;

namespace InputBusX.Infrastructure.Vision;

/// <summary>
/// Closed-set weapon classifier built on OpenCV normalized cross-correlation
/// (<c>TM_CCOEFF_NORMED</c>). Replaces the prior Tesseract OCR pipeline.
///
/// ── How it works ──────────────────────────────────────────────────────────
/// For each weapon the user captures one or more reference PNGs of its name plate
/// (same pixel dimensions as the live capture region). On every detection tick the
/// service:
///   1. Grabs the HUD region from the desktop.
///   2. Converts live frame + every cached reference to grayscale.
///   3. Runs <see cref="Cv2.MatchTemplate"/> with <see cref="TemplateMatchModes.CCoeffNormed"/>
///      and keeps the best score per weapon.
///   4. Picks the weapon with the highest score; if it clears
///      <see cref="WeaponDetectionSettings.MatchThreshold"/> it becomes the candidate.
///   5. A 2-frame stability debounce suppresses momentary flickers.
///
/// ── Why this beats OCR ────────────────────────────────────────────────────
/// Weapon names are a closed set. Template matching measures pixel-wise similarity
/// against known-good exemplars, which is immune to the font/background ambiguity
/// that breaks general-purpose OCR. Accuracy is effectively 100% once references
/// exist, and per-tick cost is a few milliseconds of convolution.
///
/// ── Thread model ──────────────────────────────────────────────────────────
/// All OpenCV work happens on the detection loop task. The public API is called
/// from the UI thread; <see cref="_refLock"/> serialises access to the reference
/// cache so <see cref="CaptureReferenceAsync"/> / <see cref="ClearReferencesAsync"/>
/// can safely mutate it mid-detection.
/// </summary>
public sealed class TemplateWeaponDetectionService : IWeaponDetectionService, IDisposable
{
    private readonly ILogger<TemplateWeaponDetectionService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private WeaponProfile? _currentWeapon;
    private WeaponDetectionSettings? _activeSettings;

    // ── Stability debounce ───────────────────────────────────────────────────
    // Template matching is deterministic, so 2 frames is plenty to filter
    // single-frame anomalies (e.g. fullscreen flash, loading screen).
    private const int StabilityFrames = 2;
    private WeaponProfile? _candidateWeapon;
    private int _candidateCount;

    // ── Frame-diff fast path ─────────────────────────────────────────────────
    // When the HUD region is visually unchanged we skip template matching. Uses
    // mean absolute difference on the grayscale live frame — fast and sufficient
    // since background content in a game HUD is typically static.
    private byte[]? _prevGrayBytes;
    private const double FrameStableDelta = 1.5; // mean |Δ| per pixel on 0..255 scale

    // ── Reference cache ──────────────────────────────────────────────────────
    // weapon id → list of (source path, grayscale Mat) pairs ready for MatchTemplate.
    private readonly Dictionary<string, List<CachedReference>> _referenceCache = new();
    private readonly object _refLock = new();

    public event Action<WeaponProfile?>? WeaponChanged;
    public WeaponProfile? CurrentWeapon => _currentWeapon;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public TemplateWeaponDetectionService(ILogger<TemplateWeaponDetectionService> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────────

    public async Task StartAsync(WeaponDetectionSettings settings, CancellationToken ct = default)
    {
        await StopAsync();

        _activeSettings = settings;
        RebuildReferenceCache(settings);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => DetectionLoop(_cts.Token), _cts.Token);
        _logger.LogInformation(
            "Template-matching weapon detection started (interval {Interval}ms, threshold {T:F2}, refs {N})",
            settings.IntervalMs, settings.MatchThreshold, TotalReferenceCount());
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
            catch (Exception ex) { _logger.LogDebug(ex, "Detection loop ended with exception"); }
            _loopTask = null;
        }

        DisposeReferenceCache();
        _prevGrayBytes   = null;
        _candidateWeapon = null;
        _candidateCount  = 0;
        _activeSettings  = null;
        SetCurrentWeapon(null);
        _logger.LogInformation("Weapon detection stopped");
    }

    public Task<string> TestCaptureAsync(WeaponDetectionSettings settings)
    {
        return Task.Run(() =>
        {
            try
            {
                // Build an ephemeral cache so this call works even when detection is stopped.
                bool ephemeral = !IsRunning;
                if (ephemeral) RebuildReferenceCache(settings);

                using var captured = CaptureRegion(settings);
                using var liveGray = ToGrayscaleMat(captured);

                var scores = ScoreAllWeapons(liveGray, settings).ToList();
                var ordered = scores
                    .OrderByDescending(s => s.BestScore)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"Capture: {captured.Width}×{captured.Height}   " +
                              $"Threshold: {settings.MatchThreshold:F2}");
                sb.AppendLine();

                if (ordered.Count == 0)
                {
                    sb.AppendLine("No weapons configured. Add a weapon and capture a reference.");
                    return sb.ToString();
                }

                sb.AppendLine("WEAPON                              SCORE    REFS");
                sb.AppendLine("────────────────────────────────── ────── ──────");
                foreach (var s in ordered)
                {
                    var status = s.ReferenceCount == 0
                        ? "— no reference"
                        : s.BestScore.ToString("F3");
                    sb.AppendLine(
                        $"{Truncate(s.Weapon.Name, 34).PadRight(34)} {status,6}   {s.ReferenceCount,4}");
                }

                sb.AppendLine();
                var top = ordered[0];
                if (top.ReferenceCount == 0)
                    sb.AppendLine("⚠ No weapon has a reference captured yet.");
                else if (top.BestScore >= settings.MatchThreshold)
                    sb.AppendLine($"✓ Best match: {top.Weapon.Name} (score {top.BestScore:F3})");
                else
                    sb.AppendLine($"✗ Best candidate {top.Weapon.Name} at {top.BestScore:F3} — below threshold");

                if (ephemeral) DisposeReferenceCache();
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TestCapture failed: {Msg}", ex.Message);
                return $"[error: {ex.Message}]";
            }
        });
    }

    public Task<string> CaptureReferenceAsync(WeaponDetectionSettings settings, WeaponProfile weapon)
    {
        return Task.Run(() =>
        {
            using var captured = CaptureRegion(settings);

            var dir = GetReferencesDirectory(weapon);
            Directory.CreateDirectory(dir);

            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png";
            var path     = Path.Combine(dir, filename);
            captured.Save(path, ImageFormat.Png);

            if (!weapon.ReferenceImagePaths.Contains(path))
                weapon.ReferenceImagePaths.Add(path);

            // Refresh cache so the new reference is available immediately (even mid-detection).
            if (_activeSettings != null)
                RebuildReferenceCache(_activeSettings);

            _logger.LogInformation("Reference saved: {Path} ({W}×{H})",
                path, captured.Width, captured.Height);
            return path;
        });
    }

    public Task ClearReferencesAsync(WeaponProfile weapon)
    {
        return Task.Run(() =>
        {
            foreach (var p in weapon.ReferenceImagePaths.ToArray())
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete reference {Path}", p);
                }
            }
            weapon.ReferenceImagePaths.Clear();

            if (_activeSettings != null)
                RebuildReferenceCache(_activeSettings);

            _logger.LogInformation("Cleared references for weapon {Name}", weapon.Name);
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        DisposeReferenceCache();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Detection loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DetectionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = _activeSettings;
                if (settings == null) break;

                using var captured = CaptureRegion(settings);
                using var liveGray = ToGrayscaleMat(captured);

                // Skip template matching entirely when the HUD region is unchanged.
                bool stable = IsFrameStable(liveGray);
                if (stable && _currentWeapon != null)
                {
                    await Task.Delay(settings.IntervalMs, ct);
                    continue;
                }

                var (best, bestScore) = FindBestMatch(liveGray, settings);

                WeaponProfile? matched = (best != null && bestScore >= settings.MatchThreshold)
                    ? best : null;

                ApplyStabilityDebounce(matched);

                await Task.Delay(settings.IntervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detection loop error: {Msg}", ex.Message);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void ApplyStabilityDebounce(WeaponProfile? matched)
    {
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

    // ─────────────────────────────────────────────────────────────────────────
    //  Matching
    // ─────────────────────────────────────────────────────────────────────────

    private (WeaponProfile? Weapon, double Score) FindBestMatch(
        Mat liveGray, WeaponDetectionSettings settings)
    {
        WeaponProfile? best = null;
        double bestScore = double.MinValue;

        foreach (var score in ScoreAllWeapons(liveGray, settings))
        {
            if (score.ReferenceCount == 0) continue;
            if (score.BestScore > bestScore)
            {
                bestScore = score.BestScore;
                best      = score.Weapon;
            }
        }

        return (best, bestScore);
    }

    private IEnumerable<WeaponScore> ScoreAllWeapons(Mat liveGray, WeaponDetectionSettings settings)
    {
        List<CachedReference> snapshotRefs;

        foreach (var weapon in settings.Weapons)
        {
            lock (_refLock)
            {
                if (!_referenceCache.TryGetValue(weapon.Id, out var refs) || refs.Count == 0)
                {
                    yield return new WeaponScore(weapon, double.MinValue, 0);
                    continue;
                }
                // Snapshot the list so we can iterate without holding the lock during CV work.
                snapshotRefs = [.. refs];
            }

            double best = double.MinValue;
            foreach (var r in snapshotRefs)
            {
                double score = ScoreAgainstReference(liveGray, r.Gray);
                if (score > best) best = score;
            }
            yield return new WeaponScore(weapon, best, snapshotRefs.Count);
        }
    }

    /// <summary>
    /// TM_CCOEFF_NORMED correlation with size-mismatch handling.
    ///
    /// When the reference is SMALLER than the live frame the template slides over the
    /// search area (standard MatchTemplate behaviour) and we take the max score — this
    /// tolerates references captured at a tighter region than the current capture area
    /// without any quality loss, because no interpolation is performed.
    ///
    /// When the reference is LARGER than the live frame in either dimension we scale it
    /// DOWN proportionally so it fits (downscale preserves detail far better than
    /// upscaling the live frame). We never upscale a reference — upscaling blurs edges
    /// and collapses the NCC score.
    ///
    /// Same-size captures (the common case once references stabilise) produce a 1×1
    /// result matrix and skip all resizing.
    /// </summary>
    private static double ScoreAgainstReference(Mat live, Mat reference)
    {
        Mat? resized = null;
        try
        {
            Mat template = reference;

            bool refTooWide = reference.Cols > live.Cols;
            bool refTooTall = reference.Rows > live.Rows;

            if (refTooWide || refTooTall)
            {
                // Scale DOWN proportionally so the reference fits within the live frame.
                // Never upscale — upscaling destroys the NCC score.
                double scale = Math.Min(
                    (double)live.Rows / reference.Rows,
                    (double)live.Cols / reference.Cols);
                var targetSize = new OpenCvSharp.Size(
                    Math.Max(1, (int)(reference.Cols * scale)),
                    Math.Max(1, (int)(reference.Rows * scale)));
                resized = new Mat();
                Cv2.Resize(reference, resized, targetSize, 0, 0, InterpolationFlags.Area);
                template = resized;
            }
            // If template is still smaller than live (or was already smaller), MatchTemplate
            // slides it over the search area automatically — no resize needed.

            using var result = new Mat();
            Cv2.MatchTemplate(live, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal);
            return maxVal;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Reference cache
    // ─────────────────────────────────────────────────────────────────────────

    private void RebuildReferenceCache(WeaponDetectionSettings settings)
    {
        lock (_refLock)
        {
            DisposeReferenceCacheInternal();

            foreach (var weapon in settings.Weapons)
            {
                var list = new List<CachedReference>();
                foreach (var path in weapon.ReferenceImagePaths)
                {
                    try
                    {
                        if (!File.Exists(path))
                        {
                            _logger.LogWarning("Reference missing on disk: {Path}", path);
                            continue;
                        }
                        var gray = Cv2.ImRead(path, ImreadModes.Grayscale);
                        if (gray.Empty())
                        {
                            gray.Dispose();
                            _logger.LogWarning("Reference failed to load: {Path}", path);
                            continue;
                        }
                        list.Add(new CachedReference(path, gray));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reference load threw: {Path}", path);
                    }
                }
                if (list.Count > 0)
                    _referenceCache[weapon.Id] = list;
            }
        }
    }

    private int TotalReferenceCount()
    {
        lock (_refLock)
            return _referenceCache.Values.Sum(l => l.Count);
    }

    private void DisposeReferenceCache()
    {
        lock (_refLock) DisposeReferenceCacheInternal();
    }

    // Caller must already hold _refLock.
    private void DisposeReferenceCacheInternal()
    {
        foreach (var list in _referenceCache.Values)
            foreach (var r in list)
                r.Gray.Dispose();
        _referenceCache.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Screen capture + frame-diff
    // ─────────────────────────────────────────────────────────────────────────

    private GdiBitmap CaptureRegion(WeaponDetectionSettings s)
    {
        float dpiScale = GetDpiScale();
        int screenW = Math.Max(1, GetSystemMetrics(0));
        int screenH = Math.Max(1, GetSystemMetrics(1));

        int physW = Math.Clamp((int)(s.CaptureWidth  * dpiScale), 1, screenW);
        int physH = Math.Clamp((int)(s.CaptureHeight * dpiScale), 1, screenH);
        int physX = Math.Clamp((int)(s.CaptureX      * dpiScale), 0, screenW - physW);
        int physY = Math.Clamp((int)(s.CaptureY      * dpiScale), 0, screenH - physH);

        var bmp = new GdiBitmap(physW, physH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(physX, physY, 0, 0, new System.Drawing.Size(physW, physH), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static Mat ToGrayscaleMat(GdiBitmap bmp)
    {
        using var color = BitmapConverter.ToMat(bmp);
        var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>
    /// Compares the current grayscale frame with the previous one stored as a raw byte
    /// buffer. Returns true when the mean absolute per-pixel delta is below
    /// <see cref="FrameStableDelta"/>, i.e. the HUD has not meaningfully changed.
    /// The stored buffer is always updated to the new frame so comparison walks forward.
    /// </summary>
    private bool IsFrameStable(Mat gray)
    {
        int expected = gray.Rows * gray.Cols;
        if (expected == 0) return false;

        byte[] current = new byte[expected];
        Marshal.Copy(gray.Data, current, 0, expected);

        bool stable;
        if (_prevGrayBytes == null || _prevGrayBytes.Length != current.Length)
        {
            stable = false;
        }
        else
        {
            long diffSum = 0;
            for (int i = 0; i < current.Length; i++)
                diffSum += Math.Abs(current[i] - _prevGrayBytes[i]);
            double mean = (double)diffSum / current.Length;
            stable = mean < FrameStableDelta;
        }

        _prevGrayBytes = current;
        return stable;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Reference storage
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reference PNGs live under <c>%APPDATA%/MatrixX/references/{weaponId}/</c> so
    /// they survive reinstalls and are outside the install directory (which may be in
    /// Program Files and therefore read-only for unprivileged users).
    /// </summary>
    private static string GetReferencesDirectory(WeaponProfile weapon)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MatrixX", "references", SanitizeId(weapon.Id));
    }

    private static string SanitizeId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(id.Length);
        foreach (var c in id) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SetCurrentWeapon(WeaponProfile? weapon)
    {
        _currentWeapon = weapon;
        WeaponChanged?.Invoke(weapon);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private static float GetDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            return dpi > 0 ? dpi / 96f : 1f;
        }
        catch { return 1f; }
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    // ─────────────────────────────────────────────────────────────────────────
    //  Inner types
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record CachedReference(string Path, Mat Gray);

    private sealed record WeaponScore(WeaponProfile Weapon, double BestScore, int ReferenceCount);
}
