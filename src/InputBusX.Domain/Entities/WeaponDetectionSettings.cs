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

    /// <summary>
    /// When true, every OCR frame writes the raw capture + all pre-processing
    /// variants to <c>%TEMP%/matrixx-ocr-debug/</c> so you can inspect what
    /// Tesseract actually sees. Leave this OFF in production — it's I/O-heavy.
    /// </summary>
    public bool DebugSaveImages { get; set; } = false;

    public List<WeaponProfile> Weapons { get; set; } = [];
}
