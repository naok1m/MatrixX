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

    // ── CrowBar (CrowBar macro type) ────────────────────────────────────
    /// <summary>Cooperative anti-recoil configuration for <see cref="MacroType.CrowBar"/>.</summary>
    public CrowBarConfig CrowBar { get; set; } = new();

    // ── Custom Script (Custom macro type) ───────────────────────────────
    /// <summary>Cronus Zen-style step-by-step script for <see cref="MacroType.Custom"/>.</summary>
    public ScriptDefinition Script { get; set; } = new();

    // ── InstaDropShot / FastDrop ─────────────────────────────────────────
    /// <summary>Button used to crouch/prone (default B). Used by InstaDropShot and FastDrop.</summary>
    public GamepadButton CrouchButton { get; set; } = GamepadButton.B;

    // ── JumpShot ─────────────────────────────────────────────────────────
    /// <summary>Button used to jump (default A). Used by JumpShot.</summary>
    public GamepadButton JumpButton { get; set; } = GamepadButton.A;
    /// <summary>Interval between jumps in ms (prevents spam). Used by JumpShot.</summary>
    public int JumpIntervalMs { get; set; } = 500;

    // ── StrafeShot ───────────────────────────────────────────────────────
    /// <summary>How far the left stick strafes (0..1, normalised). Used by StrafeShot.</summary>
    public double StrafeAmplitude { get; set; } = 0.60;
    /// <summary>How fast the strafe oscillates in ms per side. Used by StrafeShot.</summary>
    public int StrafeIntervalMs { get; set; } = 120;

    // ── HoldBreath ───────────────────────────────────────────────────────
    /// <summary>Stick button to press for holding breath (L3 or R3). Used by HoldBreath.</summary>
    public GamepadButton BreathButton { get; set; } = GamepadButton.LeftThumb;

    // ── SlideCancel ──────────────────────────────────────────────────────
    /// <summary>Button that initiates the slide (default B). Used by SlideCancel.</summary>
    public GamepadButton SlideButton { get; set; } = GamepadButton.B;
    /// <summary>Delay in ms after the slide button press before the cancel re-press fires.</summary>
    public int SlideCancelDelayMs { get; set; } = 180;
    /// <summary>Button to cancel the slide (default B again, or crouch). Leave same as SlideButton for a re-press cancel.</summary>
    public GamepadButton SlideCancelButton { get; set; } = GamepadButton.B;
}

public sealed class MacroStep
{
    public GamepadButton? ButtonPress { get; set; }
    public GamepadButton? ButtonRelease { get; set; }
    public AnalogAxis? Axis { get; set; }
    public short AxisValue { get; set; }
    public int DelayAfterMs { get; set; } = 16;
}
