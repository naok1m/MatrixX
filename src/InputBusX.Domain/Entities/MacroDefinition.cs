using InputBusX.Domain.Enums;

namespace InputBusX.Domain.Entities;

public sealed class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string Name { get; set; }
    public MacroType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }

    // Activation
    public GamepadButton? ActivationButton { get; set; }
    public AnalogAxis? ActivationAxis { get; set; }
    public bool ToggleMode { get; set; }

    // Timing
    public int DelayMs { get; set; }
    public int IntervalMs { get; set; } = 16;
    public int DurationMs { get; set; }
    public bool Loop { get; set; }

    // Intensity & randomization
    public double Intensity { get; set; } = 1.0;
    public double RandomizationFactor { get; set; }

    // Auto ping specific
    public GamepadButton? PingButton { get; set; }

    // Remap specific
    public GamepadButton? SourceButton { get; set; }
    public GamepadButton? TargetButton { get; set; }
    public AnalogAxis? SourceAxis { get; set; }
    public AnalogAxis? TargetAxis { get; set; }

    // No-recoil specific
    public TriggerSource TriggerSource { get; set; } = TriggerSource.RightTrigger;
    public int RecoilCompensationX { get; set; }
    public int RecoilCompensationY { get; set; } = -5000;

    // Aim assist buff specific
    public int FlickStrength { get; set; } = 32767;
    public int FlickIntervalMs { get; set; } = 8;

    // Sequence steps
    public List<MacroStep> Steps { get; set; } = [];

    // ── Scripted motion (ScriptedShape macro type) ───────────────────────
    /// <summary>Parametric stick motion used by <see cref="MacroType.ScriptedShape"/>.</summary>
    public MotionScript Motion { get; set; } = new();

    // ── Head Assist (HeadAssist macro type) ──────────────────────────────
    /// <summary>Distance-adaptive flick configuration for <see cref="MacroType.HeadAssist"/>.</summary>
    public HeadAssistConfig HeadAssist { get; set; } = new();

    // ── Progressive Recoil (ProgressiveRecoil macro type) ───────────────
    /// <summary>3-phase recoil compensation for <see cref="MacroType.ProgressiveRecoil"/>.</summary>
    public ProgressiveRecoilConfig ProgressiveRecoil { get; set; } = new();

    // ── Tracking Assist (TrackingAssist macro type) ─────────────────────
    /// <summary>Orbital tracking overlay for <see cref="MacroType.TrackingAssist"/>.</summary>
    public TrackingAssistConfig TrackingAssist { get; set; } = new();
}

public sealed class MacroStep
{
    public GamepadButton? ButtonPress { get; set; }
    public GamepadButton? ButtonRelease { get; set; }
    public AnalogAxis? Axis { get; set; }
    public short AxisValue { get; set; }
    public int DelayAfterMs { get; set; } = 16;
}
