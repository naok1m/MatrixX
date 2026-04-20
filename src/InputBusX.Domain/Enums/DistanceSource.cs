namespace InputBusX.Domain.Enums;

/// <summary>
/// Signal the Head Assist distance estimator listens to in order to pick
/// between <see cref="DistanceLevel.Short"/>, <see cref="DistanceLevel.Medium"/>
/// and <see cref="DistanceLevel.Long"/> presets. The "Auto" mode fuses every
/// available signal — that is the mode that makes MatrixX feel closer to a
/// real aim trainer than a static Zen macro.
/// </summary>
public enum DistanceSource
{
    /// <summary>Fire-trigger dwell time — a tap is close, a burst is medium, sustained is long.</summary>
    TriggerHoldTime,

    /// <summary>Aim-stick deflection magnitude — flicks across the screen imply longer range tracking.</summary>
    AimStickDeflection,

    /// <summary>Weapon-profile recoil magnitude — heavier recoil weapons bias toward longer range.</summary>
    RecoilMagnitude,

    /// <summary>User cycles level explicitly via a dedicated button.</summary>
    Manual,

    /// <summary>Weighted fusion of all three auto signals (default).</summary>
    Auto,
}
