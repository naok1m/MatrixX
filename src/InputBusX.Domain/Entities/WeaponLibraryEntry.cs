namespace InputBusX.Domain.Entities;

/// <summary>
/// A read-only pre-configured weapon from the built-in weapon database.
/// Users can import these into their WeaponDetectionSettings as WeaponProfiles.
/// </summary>
public sealed class WeaponLibraryEntry
{
    public string Id { get; set; } = "";

    /// <summary>Game this weapon belongs to (e.g. "Warzone", "Apex Legends").</summary>
    public string Game { get; set; } = "";

    /// <summary>Weapon category (e.g. "Assault Rifle", "SMG", "Sniper").</summary>
    public string Category { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>OCR keywords that identify this weapon in the HUD.</summary>
    public List<string> Keywords { get; set; } = [];

    public int RecoilCompensationX { get; set; } = 0;
    public int RecoilCompensationY { get; set; } = -5000;
    public double Intensity { get; set; } = 1.0;

    /// <summary>Converts this library entry into a user-owned WeaponProfile.</summary>
    public WeaponProfile ToWeaponProfile() => new()
    {
        Id       = Guid.NewGuid().ToString("N")[..8],
        Name     = Name,
        Keywords = new List<string>(Keywords),
        RecoilCompensationX = RecoilCompensationX,
        RecoilCompensationY = RecoilCompensationY,
        Intensity           = Intensity
    };
}
