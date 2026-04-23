using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// Root container for a Cronus Zen-style custom macro script.
/// Stored on <see cref="MacroDefinition.Script"/> and used exclusively
/// by the <see cref="MacroType.Custom"/> type.
/// </summary>
public sealed class ScriptDefinition
{
    /// <summary>Ordered list of steps that form the script.</summary>
    public List<ScriptStep> Steps { get; set; } = [];

    /// <summary>How the script triggers (while-held, on-press, toggle).</summary>
    public ScriptTriggerKind TriggerMode { get; set; } = ScriptTriggerKind.WhileHeld;

    /// <summary>Whether the entire script loops from the end back to step 0 after completion.</summary>
    public bool AutoLoop { get; set; } = true;

    /// <summary>Global speed multiplier applied to all Wait durations (1.0 = normal).</summary>
    public double SpeedMultiplier { get; set; } = 1.0;

    /// <summary>Descriptive name for the combo/script.</summary>
    public string Description { get; set; } = "";
}
