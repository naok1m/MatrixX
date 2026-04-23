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

    /// <summary>Auto-fire + no-recoil combined — rapid trigger presses with per-weapon recoil compensation.</summary>
    AutoFireNoRecoil,

    /// <summary>Instantly goes prone (crouch button held) on activation — bypasses the normal crouch→prone delay.</summary>
    InstaDropShot,

    /// <summary>Player jumps automatically when shooting or aiming — user configures the trigger.</summary>
    JumpShot,

    /// <summary>Strafes left/right while shooting — alternating left stick X input.</summary>
    StrafeShot,

    /// <summary>Auto-holds breath (L3/R3) when aiming with a sniper — presses and holds the chosen stick button while ADS.</summary>
    HoldBreath,

    /// <summary>Detects a slide and auto-cancels it after a configurable delay by re-pressing the slide button.</summary>
    SlideCancel,

    /// <summary>ADS + quick crouch tap = instant drop-shot while firing — combines aim and prone in one press.</summary>
    FastDrop,

    /// <summary>Cooperative anti-recoil (CrowBar mechanic) — amplifies manual stick-down input with a fixed HTG value. Rapido (40%) or Padrao (90%) modes.</summary>
    CrowBar,

    /// <summary>Cronus Zen-style custom scripted macro — step-by-step sequencer with button presses, axis control, timing, and loops.</summary>
    Custom
}
