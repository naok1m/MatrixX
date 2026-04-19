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
    /// Captures the configured region once and runs a single detection pass, returning
    /// a multi-line diagnostic that lists the score of every weapon in the library.
    /// Useful for dialing in <see cref="WeaponDetectionSettings.MatchThreshold"/>.
    /// </summary>
    Task<string> TestCaptureAsync(WeaponDetectionSettings settings);

    /// <summary>
    /// Captures the configured region and saves it as a new reference image for
    /// <paramref name="weapon"/>. The absolute path is appended to
    /// <see cref="WeaponProfile.ReferenceImagePaths"/> and returned. Safe to call while
    /// detection is running — the engine's reference cache is refreshed atomically.
    /// </summary>
    Task<string> CaptureReferenceAsync(WeaponDetectionSettings settings, WeaponProfile weapon);

    /// <summary>
    /// Deletes every reference image file for <paramref name="weapon"/> from disk and
    /// clears <see cref="WeaponProfile.ReferenceImagePaths"/>.
    /// </summary>
    Task ClearReferencesAsync(WeaponProfile weapon);
}
