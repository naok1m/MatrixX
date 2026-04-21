namespace InputBusX.Domain.Enums;

public enum MacroType
{
    NoRecoil,
    AutoFire,
    AutoPing,
    Remap,
    Sequence,
    Toggle,
    AimAssistBuff,

    /// <summary>Distance-adaptive head-height flick, tuned per engagement range.</summary>
    HeadAssist,

    /// <summary>Drives a thumbstick along a parametric shape (circle, oval, diagonal oval, flick).</summary>
    ScriptedShape,

    /// <summary>Progressive no-recoil in 3 phases (start/mid/end) based on ammo count and fire time.</summary>
    ProgressiveRecoil,

    /// <summary>Additive orbital motion that follows stick movement — simulates tracking for aim assist buff.</summary>
    TrackingAssist,

    Custom
}
