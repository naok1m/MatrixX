using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IWeaponDetectionService
{
    /// <summary>Fires whenever the detected weapon changes (null = no weapon matched).</summary>
    event Action<WeaponProfile?>? WeaponChanged;

    WeaponProfile? CurrentWeapon { get; }
    bool IsRunning { get; }

    Task StartAsync(WeaponDetectionSettings settings, CancellationToken ct = default);
    Task StopAsync();

    /// <summary>
    /// Captures the configured region once and returns the raw OCR text — useful for
    /// verifying coordinates without starting the full detection loop.
    /// </summary>
    Task<string> TestCaptureAsync(WeaponDetectionSettings settings);
}
