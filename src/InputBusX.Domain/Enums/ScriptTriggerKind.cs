namespace InputBusX.Domain.Enums;

/// <summary>How a custom script activates.</summary>
public enum ScriptTriggerKind
{
    /// <summary>Execute while the activation button is held.</summary>
    WhileHeld,

    /// <summary>Execute once when the activation button is pressed (rising edge).</summary>
    OnPress,

    /// <summary>Toggle on/off with the activation button.</summary>
    Toggle,
}
