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

    /// <summary>How often to run OCR, in milliseconds.</summary>
    public int IntervalMs { get; set; } = 500;

    public List<WeaponProfile> Weapons { get; set; } = [];
}
