namespace InputBusX.Domain.Entities;

public sealed class WeaponProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New Weapon";

    /// <summary>
    /// OCR must find at least one of these keywords (case-insensitive) to match this weapon.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    public int RecoilCompensationX { get; set; } = 0;
    public int RecoilCompensationY { get; set; } = -5000;
    public double Intensity { get; set; } = 1.0;

    /// <summary>When true, all AutoFire macros use this weapon's rapid-fire interval.</summary>
    public bool RapidFireEnabled { get; set; } = false;

    /// <summary>Interval between simulated trigger presses in ms (lower = faster).</summary>
    public int RapidFireIntervalMs { get; set; } = 50;
}
