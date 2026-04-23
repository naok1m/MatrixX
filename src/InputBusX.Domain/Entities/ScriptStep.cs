using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

/// <summary>
/// A single step in a Cronus Zen-style custom script. Each step performs one
/// atomic action (press, release, set axis, wait, loop). Steps execute
/// sequentially; simultaneous button presses are expressed as consecutive
/// PressButton steps with no Wait between them.
/// </summary>
public sealed class ScriptStep
{
    /// <summary>What this step does.</summary>
    public ScriptActionKind Action { get; set; } = ScriptActionKind.Wait;

    /// <summary>Button targeted by PressButton / ReleaseButton actions.</summary>
    public GamepadButton? Button { get; set; }

    /// <summary>Axis targeted by SetAxis / SetTrigger action.</summary>
    public AnalogAxis? Axis { get; set; }

    /// <summary>Value for SetAxis (-32767..32767) or SetTrigger (0..255).</summary>
    public short Value { get; set; }

    /// <summary>Duration in ms for Wait steps.</summary>
    public int DurationMs { get; set; } = 16;

    /// <summary>For LoopBack: the step index to jump back to.</summary>
    public int LoopTargetIndex { get; set; }

    /// <summary>For LoopBack/LoopStart: number of iterations. 0 = infinite (while trigger held).</summary>
    public int RepeatCount { get; set; }

    /// <summary>User-visible label/comment for this step in the visual editor.</summary>
    public string Label { get; set; } = "";

    /// <summary>Whether this step is disabled (skipped during execution).</summary>
    public bool Disabled { get; set; }
}
