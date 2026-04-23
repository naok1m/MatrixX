namespace InputBusX.Domain.Enums;

/// <summary>Action types available in a custom script step (Cronus Zen-style).</summary>
public enum ScriptActionKind
{
    /// <summary>Press a gamepad button (transition to held state).</summary>
    PressButton,

    /// <summary>Release a gamepad button (transition to released state).</summary>
    ReleaseButton,

    /// <summary>Set an analog axis to a specific value (-32767..32767).</summary>
    SetAxis,

    /// <summary>Set a trigger value (0..255).</summary>
    SetTrigger,

    /// <summary>Wait for a duration in ms before continuing to the next step.</summary>
    Wait,

    /// <summary>Jump back to a target step index. Combined with RepeatCount for finite loops.</summary>
    LoopBack,

    /// <summary>Marker for the start of a loop block. Pairs with LoopBack in the visual editor.</summary>
    LoopStart,
}
