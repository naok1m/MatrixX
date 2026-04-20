namespace InputBusX.Domain.Entities;

public sealed class WeaponProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New Weapon";

    /// <summary>
    /// Legacy OCR keywords. Kept for backward-compatibility with settings files from
    /// v1.x but unused by the template-matching detection pipeline. New profiles should
    /// rely on <see cref="ReferenceImagePaths"/> instead.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// Absolute paths to reference screenshots of this weapon's HUD name plate. Each
    /// reference is captured at the current detection-region size; at runtime the live
    /// capture is correlated against every reference (OpenCV TM_CCOEFF_NORMED) and the
    /// best score across the set is used for matching. Adding multiple references
    /// (e.g. ADS vs hip-fire, different lighting) improves robustness.
    /// </summary>
    public List<string> ReferenceImagePaths { get; set; } = [];

    public int RecoilCompensationX { get; set; } = 0;
    public int RecoilCompensationY { get; set; } = -5000;
    public double Intensity { get; set; } = 1.0;

    /// <summary>When true, all AutoFire macros use this weapon's rapid-fire interval.</summary>
    public bool RapidFireEnabled { get; set; } = false;

    /// <summary>Interval between simulated trigger presses in ms (lower = faster).</summary>
    public int RapidFireIntervalMs { get; set; } = 50;

    // ── Per-weapon capture region (optional override) ─────────────────────────
    /// <summary>When true the detection loop captures this weapon from its own
    /// region instead of the global one. Useful when weapon names differ in width
    /// so a tight crop per weapon improves the signal-to-noise ratio. When false
    /// (default), the global <c>WeaponDetectionSettings</c> region is used.</summary>
    public bool UseCustomRegion { get; set; } = false;

    public int CaptureX { get; set; } = 1700;
    public int CaptureY { get; set; } = 950;
    public int CaptureWidth { get; set; } = 300;
    public int CaptureHeight { get; set; } = 60;
}
