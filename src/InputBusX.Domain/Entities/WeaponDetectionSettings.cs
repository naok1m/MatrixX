namespace InputBusX.Domain.Entities;

public sealed class WeaponDetectionSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Screen X coordinate of the capture region (top-left corner).</summary>
    public int CaptureX { get; set; } = 1700;

    /// <summary>Screen Y coordinate of the capture region (top-left corner).</summary>
    public int CaptureY { get; set; } = 950;

    public int CaptureWidth { get; set; } = 300;
    public int CaptureHeight { get; set; } = 60;

    /// <summary>How often the detection loop runs, in milliseconds.</summary>
    public int IntervalMs { get; set; } = 250;

    /// <summary>
    /// Normalized cross-correlation score (TM_CCOEFF_NORMED) that a reference must
    /// reach to count as a positive match. Range [0..1]; higher = stricter.
    ///   0.70  very loose — may accept similar-looking weapons
    ///   0.80  balanced — recommended default
    ///   0.90  strict — only near-pixel-identical matches
    /// </summary>
    public double MatchThreshold { get; set; } = 0.80;

    /// <summary>
    /// When true, every detection frame writes the live capture + per-weapon scores
    /// to <c>%TEMP%/matrixx-detection-debug/</c>. Leave OFF in production — disk I/O heavy.
    /// </summary>
    public bool DebugSaveImages { get; set; } = false;

    public List<WeaponProfile> Weapons { get; set; } = [];
}
