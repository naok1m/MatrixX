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

    Custom
}
