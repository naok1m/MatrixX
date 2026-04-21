using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using InputBusX.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace InputBusX.Application.MacroEngine;

public sealed class MacroProcessor : IMacroProcessor
{
    private readonly ILogger<MacroProcessor> _logger;
    private readonly Dictionary<string, MacroRuntime> _runtimes = new();
    private readonly Random _random = new();

    // When non-null, all NoRecoil macros use this weapon's compensation values
    private WeaponProfile? _weaponProfile;

    public MacroProcessor(ILogger<MacroProcessor> logger)
    {
        _logger = logger;
    }

    public void SetWeaponProfile(WeaponProfile? profile)
    {
        _weaponProfile = profile;
        _logger.LogDebug("Weapon profile set to: {Name}", profile?.Name ?? "none");
    }

    public GamepadState Process(GamepadState input, IReadOnlyList<MacroDefinition> activeMacros)
    {
        var state = input.Clone();

        foreach (var macro in activeMacros)
        {
            state = macro.Type switch
            {
                MacroType.NoRecoil => ProcessNoRecoil(state, macro),
                MacroType.AutoFire => ProcessAutoFire(state, macro),
                MacroType.AutoPing => ProcessAutoPing(state, macro),
                MacroType.Remap => ProcessRemap(state, macro),
                MacroType.Sequence => ProcessSequence(state, macro),
                MacroType.Toggle => ProcessToggle(state, macro),
                MacroType.AimAssistBuff => ProcessAimAssistBuff(state, macro),
                MacroType.HeadAssist => ProcessHeadAssist(state, macro),
                MacroType.ScriptedShape => ProcessScriptedShape(state, macro),
                MacroType.ProgressiveRecoil => ProcessProgressiveRecoil(state, macro),
                MacroType.TrackingAssist => ProcessTrackingAssist(state, macro),
                _ => state
            };
        }

        return state;
    }

    public void Reset()
    {
        _runtimes.Clear();
    }

    private MacroRuntime GetRuntime(MacroDefinition macro)
    {
        if (!_runtimes.TryGetValue(macro.Id, out var runtime))
        {
            runtime = new MacroRuntime();
            _runtimes[macro.Id] = runtime;
        }
        return runtime;
    }

    private GamepadState ProcessNoRecoil(GamepadState state, MacroDefinition macro)
    {
        bool isShooting = macro.TriggerSource == TriggerSource.LeftTrigger
            ? state.LeftTrigger.IsPressed()
            : state.RightTrigger.IsPressed();

        bool isActive;
        if (macro.ToggleMode && macro.ActivationButton.HasValue)
        {
            // Toggle: press activation button once to enable/disable
            var runtime = GetRuntime(macro);
            bool pressed = state.IsButtonPressed(macro.ActivationButton.Value);
            if (pressed && !runtime.WasPressed)
                runtime.ToggleState = !runtime.ToggleState;
            runtime.WasPressed = pressed;
            isActive = runtime.ToggleState;
        }
        else
        {
            // Hold: active while activation button is held (or always if none set)
            isActive = !macro.ActivationButton.HasValue || state.IsButtonPressed(macro.ActivationButton.Value);
        }

        if (isActive && isShooting)
        {
            // Weapon profile overrides the macro's own recoil values when active
            int compensationX, compensationY;
            if (_weaponProfile != null)
            {
                compensationX = (int)(_weaponProfile.RecoilCompensationX * _weaponProfile.Intensity);
                compensationY = (int)(_weaponProfile.RecoilCompensationY * _weaponProfile.Intensity);
            }
            else
            {
                compensationX = (int)(macro.RecoilCompensationX * macro.Intensity);
                compensationY = (int)(macro.RecoilCompensationY * macro.Intensity);
            }

            if (macro.RandomizationFactor > 0)
            {
                var randRange = (int)(macro.RandomizationFactor * 100);
                compensationX += _random.Next(-randRange, randRange + 1);
                compensationY += _random.Next(-randRange, randRange + 1);
            }

            // Add compensation on top of current stick position (don't replace it)
            state.RightStick = new StickPosition(
                (short)Math.Clamp(state.RightStick.X + compensationX, short.MinValue, short.MaxValue),
                (short)Math.Clamp(state.RightStick.Y + compensationY, short.MinValue, short.MaxValue));
        }

        return state;
    }

    private GamepadState ProcessAutoFire(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);

        // Determine if the trigger axis (RT/LT) or a button is the activation source
        bool isTriggerAxis = macro.TriggerSource == TriggerSource.RightTrigger
                             || macro.TriggerSource == TriggerSource.LeftTrigger;
        bool held;
        if (isTriggerAxis)
        {
            held = macro.TriggerSource == TriggerSource.RightTrigger
                ? state.RightTrigger.IsPressed()
                : state.LeftTrigger.IsPressed();
        }
        else
        {
            var btn = macro.ActivationButton ?? GamepadButton.RightShoulder;
            held = state.IsButtonPressed(btn);
        }

        // If weapon profile is loaded and rapid fire is disabled on it, skip
        if (_weaponProfile != null && !_weaponProfile.RapidFireEnabled)
        {
            runtime.ToggleState = false;
            return state;
        }

        // Use weapon profile's interval when available, otherwise macro's interval
        int intervalMs = (_weaponProfile?.RapidFireEnabled == true)
            ? _weaponProfile.RapidFireIntervalMs
            : macro.IntervalMs;

        if (held)
        {
            var now = Environment.TickCount64;
            if (now - runtime.LastFireTick >= intervalMs)
            {
                runtime.ToggleState = !runtime.ToggleState;
                runtime.LastFireTick = now;
            }

            // Toggle the correct axis or button
            if (isTriggerAxis)
            {
                if (macro.TriggerSource == TriggerSource.RightTrigger)
                    state.RightTrigger = runtime.ToggleState ? TriggerValue.Full : TriggerValue.Zero;
                else
                    state.LeftTrigger = runtime.ToggleState ? TriggerValue.Full : TriggerValue.Zero;
            }
            else
            {
                var btn = macro.ActivationButton ?? GamepadButton.RightShoulder;
                state.SetButton(btn, runtime.ToggleState);
            }
        }
        else
        {
            runtime.ToggleState = false;
        }

        return state;
    }

    private GamepadState ProcessAutoPing(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var pingButton = macro.PingButton ?? GamepadButton.DPadUp;
        bool held = state.RightTrigger.IsPressed()
            && (!macro.ActivationButton.HasValue || state.IsButtonPressed(macro.ActivationButton.Value));

        if (held)
        {
            var now = Environment.TickCount64;
            var interval = macro.IntervalMs > 0 ? macro.IntervalMs : 200;
            if (now >= runtime.PulseUntilTick && now - runtime.LastFireTick >= interval)
            {
                state.SetButton(pingButton, true);
                runtime.LastFireTick = now;
                runtime.PulseUntilTick = now + 50;
            }
            else if (now < runtime.PulseUntilTick)
            {
                state.SetButton(pingButton, true);
            }
        }
        else
        {
            // Cut off any in-flight pulse, but don't override a manual press of the button
            if (runtime.PulseUntilTick > 0 && !state.IsButtonPressed(pingButton))
                state.SetButton(pingButton, false);
            runtime.PulseUntilTick = 0;
        }

        return state;
    }

    private GamepadState ProcessRemap(GamepadState state, MacroDefinition macro)
    {
        if (macro.SourceButton.HasValue && macro.TargetButton.HasValue)
        {
            bool pressed = state.IsButtonPressed(macro.SourceButton.Value);
            state.SetButton(macro.SourceButton.Value, false);
            state.SetButton(macro.TargetButton.Value, pressed);
        }

        return state;
    }

    private GamepadState ProcessSequence(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var trigger = macro.ActivationButton ?? GamepadButton.A;
        bool held = state.IsButtonPressed(trigger);

        if (held && macro.Steps.Count > 0)
        {
            // Clamp index: Steps may have shrunk since the last frame (UI edit on another thread)
            if (runtime.StepIndex >= macro.Steps.Count)
                runtime.StepIndex = 0;

            var now = Environment.TickCount64;
            var step = macro.Steps[runtime.StepIndex];

            if (now - runtime.LastFireTick >= step.DelayAfterMs)
            {
                if (step.ButtonPress.HasValue)
                    state.SetButton(step.ButtonPress.Value, true);
                if (step.ButtonRelease.HasValue)
                    state.SetButton(step.ButtonRelease.Value, false);

                runtime.LastFireTick = now;
                runtime.StepIndex = (runtime.StepIndex + 1) % macro.Steps.Count;

                if (runtime.StepIndex == 0 && !macro.Loop)
                    runtime.StepIndex = macro.Steps.Count - 1;
            }
        }
        else if (!held)
        {
            runtime.StepIndex = 0;
        }

        return state;
    }

    private GamepadState ProcessToggle(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var trigger = macro.ActivationButton ?? GamepadButton.A;
        bool pressed = state.IsButtonPressed(trigger);

        if (pressed && !runtime.WasPressed)
            runtime.ToggleState = !runtime.ToggleState;
        runtime.WasPressed = pressed;

        var target = macro.TargetButton ?? trigger;
        state.SetButton(target, runtime.ToggleState);

        return state;
    }

    private GamepadState ProcessAimAssistBuff(GamepadState state, MacroDefinition macro)
    {
        bool isShooting = macro.TriggerSource == TriggerSource.LeftTrigger
            ? state.LeftTrigger.IsPressed()
            : state.RightTrigger.IsPressed();

        var runtime = GetRuntime(macro);

        if (!isShooting)
        {
            // Reset flick direction when not shooting so next shot starts fresh
            runtime.ToggleState = false;
            runtime.LastFireTick = 0;
            return state;
        }

        var now = Environment.TickCount64;
        var interval = macro.FlickIntervalMs > 0 ? macro.FlickIntervalMs : 8;

        // Toggle flick direction every interval
        if (now - runtime.LastFireTick >= interval)
        {
            runtime.ToggleState = !runtime.ToggleState;
            runtime.LastFireTick = now;
        }

        // Apply full-deflection flick to LEFT stick X axis
        // Preserves Y (player vertical movement) untouched
        var strength = (short)Math.Clamp(
            (int)(macro.FlickStrength * macro.Intensity),
            short.MinValue, short.MaxValue);

        state.LeftStick = new StickPosition(
            runtime.ToggleState ? strength : (short)-strength,
            state.LeftStick.Y);

        return state;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ScriptedShape — drives a thumbstick along a parametric motion while
    //  the activation button (or trigger) is held. Orbital shapes loop until
    //  the trigger releases; Flick runs once per press edge.
    // ─────────────────────────────────────────────────────────────────────
    private GamepadState ProcessScriptedShape(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var motion = macro.Motion;
        bool held = IsMacroActive(state, macro, runtime);
        long now = Environment.TickCount64;

        if (!held)
        {
            runtime.MotionActivationTick = 0;
            runtime.WasPressed = false;
            return state;
        }

        // Press edge — stamp activation time so elapsedMs walks from zero
        if (runtime.MotionActivationTick == 0)
            runtime.MotionActivationTick = now;

        double elapsedMs = now - runtime.MotionActivationTick;
        var sample = MotionSampler.Evaluate(motion, elapsedMs);

        // Re-arm on completion for looping orbital shapes (Flick completes => stay silent)
        if (sample.Completed)
        {
            if (motion.Shape != ShapeKind.Flick && motion.DurationMs == 0)
            {
                // shouldn't happen — DurationMs==0 means indefinite, so Completed stays false
            }
            return state;
        }

        ApplyMotionSample(state, motion, sample, macro.Intensity);
        return state;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  HeadAssist — distance-adaptive flick fired from the fire trigger.
    //  Picks one of three presets (short / medium / long) per trigger press
    //  based on the configured DistanceSource; respects a re-fire cooldown
    //  so sustained fire doesn't stack flicks on top of each other.
    // ─────────────────────────────────────────────────────────────────────
    private GamepadState ProcessHeadAssist(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var cfg = macro.HeadAssist;
        long now = Environment.TickCount64;

        bool fireHeld = macro.TriggerSource == TriggerSource.LeftTrigger
            ? state.LeftTrigger.IsPressed()
            : state.RightTrigger.IsPressed();

        // Track press edge + hold duration for the TriggerHoldTime estimator.
        if (fireHeld)
        {
            if (runtime.TriggerDownSinceTick == 0)
                runtime.TriggerDownSinceTick = now;
        }
        else
        {
            runtime.TriggerDownSinceTick = 0;
            runtime.HeadAssistActivationTick = 0;
        }

        // Manual cycle button — rotates DistanceLevel short → medium → long → short.
        if (cfg.CycleButton.HasValue)
        {
            bool cyclePressed = state.IsButtonPressed(cfg.CycleButton.Value);
            if (cyclePressed && !runtime.WasCycleButtonPressed)
            {
                runtime.ManualLevel = runtime.ManualLevel switch
                {
                    DistanceLevel.Short  => DistanceLevel.Medium,
                    DistanceLevel.Medium => DistanceLevel.Long,
                    _ => DistanceLevel.Short,
                };
            }
            runtime.WasCycleButtonPressed = cyclePressed;
        }

        // Decide whether this frame starts a new flick.
        bool activeFlick = runtime.HeadAssistActivationTick != 0;
        bool fireEdge = fireHeld && !runtime.WasFirePressed;
        runtime.WasFirePressed = fireHeld;

        if (!activeFlick && fireHeld)
        {
            double heldMs = now - runtime.TriggerDownSinceTick;
            bool cooldownOk = (now - runtime.LastHeadAssistTick) >= cfg.ReFireCooldownMs;
            bool minHoldOk = heldMs >= cfg.MinTriggerHoldMs;
            // FireOnPress is the ADS-snap: fire immediately on the press edge.
            // Requiring minHoldOk on the edge frame (when heldMs=0) would veto it forever —
            // so minHoldOk is only gated on the cooldown re-fire path.
            bool pressFire = cfg.FireOnPress && fireEdge;
            // Cooldown re-fire is disabled when FireOnce is set — a single flick per
            // press-and-hold, user must release+press again to fire again.
            bool cooldownFire = !cfg.FireOnce && cooldownOk && minHoldOk && !fireEdge;
            bool shouldFire = pressFire || cooldownFire;

            if (shouldFire)
            {
                var level = EstimateDistance(state, cfg, runtime, heldMs);
                runtime.CurrentHeadAssistLevel = level;
                runtime.HeadAssistActivationTick = now;
                runtime.LastHeadAssistTick = now;
                _logger.LogDebug("HeadAssist firing: level={Level} heldMs={Held:F0}", level, heldMs);
            }
        }

        if (runtime.HeadAssistActivationTick == 0) return state;

        var script = runtime.CurrentHeadAssistLevel switch
        {
            DistanceLevel.Short  => cfg.ShortRange,
            DistanceLevel.Medium => cfg.MediumRange,
            _ => cfg.LongRange,
        };

        double elapsed = now - runtime.HeadAssistActivationTick;
        var sample = MotionSampler.Evaluate(script, elapsed);
        if (sample.Completed)
        {
            runtime.HeadAssistActivationTick = 0;
            return state;
        }

        ApplyMotionSample(state, script, sample, macro.Intensity);
        return state;
    }

    /// <summary>
    /// Fuses the configured <see cref="DistanceSource"/> signals into a
    /// <see cref="DistanceLevel"/>. Each auto signal produces a scalar in
    /// [0,1] (0=close, 1=long) — the Auto mode takes a weighted mean of the
    /// three signals then buckets into Short/Medium/Long.
    /// </summary>
    private DistanceLevel EstimateDistance(
        GamepadState state, HeadAssistConfig cfg, MacroRuntime runtime, double heldMs)
    {
        if (cfg.DistanceSource == DistanceSource.Manual)
            return runtime.ManualLevel;

        double ScoreTrigger()
        {
            if (heldMs <= cfg.ShortHoldMsMax)  return 0.0;
            if (heldMs >= cfg.MediumHoldMsMax) return 1.0;
            return (heldMs - cfg.ShortHoldMsMax) / (cfg.MediumHoldMsMax - cfg.ShortHoldMsMax);
        }

        double ScoreDeflection()
        {
            double mag = Math.Sqrt(
                (state.LeftStick.X / 32767.0) * (state.LeftStick.X / 32767.0) +
                (state.LeftStick.Y / 32767.0) * (state.LeftStick.Y / 32767.0));
            if (mag <= cfg.DeflectionShortMax)  return 0.0;
            if (mag >= cfg.DeflectionMediumMax) return 1.0;
            return (mag - cfg.DeflectionShortMax) / (cfg.DeflectionMediumMax - cfg.DeflectionShortMax);
        }

        double ScoreRecoil()
        {
            if (_weaponProfile == null) return 0.5;
            double recoil = Math.Abs(_weaponProfile.RecoilCompensationY) + Math.Abs(_weaponProfile.RecoilCompensationX);
            if (recoil <= cfg.RecoilShortMax)  return 0.0;
            if (recoil >= cfg.RecoilMediumMax) return 1.0;
            return (recoil - cfg.RecoilShortMax) / (cfg.RecoilMediumMax - cfg.RecoilShortMax);
        }

        double score = cfg.DistanceSource switch
        {
            DistanceSource.TriggerHoldTime     => ScoreTrigger(),
            DistanceSource.AimStickDeflection  => ScoreDeflection(),
            DistanceSource.RecoilMagnitude     => ScoreRecoil(),
            DistanceSource.Auto                => WeightedMean(
                (ScoreTrigger(),    cfg.WeightTrigger),
                (ScoreDeflection(), cfg.WeightDeflection),
                (ScoreRecoil(),     cfg.WeightRecoil)),
            _ => 0.5,
        };

        return score switch
        {
            <= 0.34 => DistanceLevel.Short,
            <= 0.66 => DistanceLevel.Medium,
            _       => DistanceLevel.Long,
        };
    }

    private static double WeightedMean(params (double Value, double Weight)[] items)
    {
        double ws = 0, ss = 0;
        foreach (var (v, w) in items)
        {
            if (w <= 0) continue;
            ss += v * w;
            ws += w;
        }
        return ws > 0 ? ss / ws : 0.5;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Progressive Recoil — 3-phase ammo-based compensation with smoothstep
    // ─────────────────────────────────────────────────────────────────────

    private GamepadState ProcessProgressiveRecoil(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var cfg = macro.ProgressiveRecoil;
        long now = Environment.TickCount64;

        bool isShooting = macro.TriggerSource switch
        {
            TriggerSource.LeftTrigger  => state.LeftTrigger.IsPressed(),
            TriggerSource.RightTrigger => state.RightTrigger.IsPressed(),
            _ => false,
        };

        // Activation gate (same as NoRecoil)
        bool isActive;
        if (macro.ToggleMode && macro.ActivationButton.HasValue)
        {
            bool pressed = state.IsButtonPressed(macro.ActivationButton.Value);
            if (pressed && !runtime.WasPressed)
                runtime.ToggleState = !runtime.ToggleState;
            runtime.WasPressed = pressed;
            isActive = runtime.ToggleState;
        }
        else
        {
            isActive = !macro.ActivationButton.HasValue
                || state.IsButtonPressed(macro.ActivationButton.Value);
        }

        if (!isActive || !isShooting)
        {
            runtime.ProgressiveFireStartTick = 0;
            return state;
        }

        // Start timer on fire edge
        if (runtime.ProgressiveFireStartTick == 0)
            runtime.ProgressiveFireStartTick = now;

        // Sensitivity: scales duration inversely and compensation inversely
        double sensMul = cfg.SensitivityScale > 0 ? cfg.SensitivityScale : 1.0;
        double effectiveDuration = cfg.FullMagDurationMs / sensMul;
        double compScale = 1.0 / sensMul;

        double elapsedMs = now - runtime.ProgressiveFireStartTick;
        double progress = Math.Clamp(elapsedMs / effectiveDuration, 0.0, 1.0);

        // Phase boundaries (split by ammo thirds)
        int total = Math.Max(cfg.TotalAmmo, 3);
        int startCount = total / 3;
        int midCount = (total - startCount) / 2;
        // Phase time boundaries (proportional to ammo split)
        double p1 = (double)startCount / total;           // ~0.333
        double p2 = (double)(startCount + midCount) / total; // ~0.667

        // Interpolate compensation X/Y based on progress through phases
        double compX, compY;
        if (progress <= p1)
        {
            // Phase 1 — pure start values
            double t = progress / p1;
            double eased = ApplyPhaseEasing(cfg.PhaseEasing, t);
            compX = Lerp(0, cfg.StartCompX, eased);
            compY = Lerp(0, cfg.StartCompY, eased);
        }
        else if (progress <= p2)
        {
            // Phase 2 — blend start → mid
            double t = (progress - p1) / (p2 - p1);
            double eased = ApplyPhaseEasing(cfg.PhaseEasing, t);
            compX = Lerp(cfg.StartCompX, cfg.MidCompX, eased);
            compY = Lerp(cfg.StartCompY, cfg.MidCompY, eased);
        }
        else
        {
            // Phase 3 — blend mid → end
            double t = (progress - p2) / (1.0 - p2);
            double eased = ApplyPhaseEasing(cfg.PhaseEasing, t);
            compX = Lerp(cfg.MidCompX, cfg.EndCompX, eased);
            compY = Lerp(cfg.MidCompY, cfg.EndCompY, eased);
        }

        // Apply sensitivity scaling to compensation
        compX *= compScale * macro.Intensity;
        compY *= compScale * macro.Intensity;

        // Add noise
        if (cfg.NoiseFactor > 0)
        {
            double noiseRange = cfg.NoiseFactor * 100.0;
            compX += _random.NextDouble() * noiseRange * 2 - noiseRange;
            compY += _random.NextDouble() * noiseRange * 2 - noiseRange;
        }

        state.RightStick = new StickPosition(
            (short)Math.Clamp(state.RightStick.X + (int)compX, short.MinValue, short.MaxValue),
            (short)Math.Clamp(state.RightStick.Y + (int)compY, short.MinValue, short.MaxValue));

        return state;
    }

    private static double ApplyPhaseEasing(EasingKind kind, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return kind switch
        {
            EasingKind.Smoothstep    => t * t * (3.0 - 2.0 * t),
            EasingKind.EaseInOutSine => 0.5 - 0.5 * Math.Cos(Math.PI * t),
            EasingKind.EaseOutCubic  => 1.0 - Math.Pow(1.0 - t, 3.0),
            EasingKind.Linear        => t,
            _ => t * t * (3.0 - 2.0 * t), // default to smoothstep
        };
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    // ─────────────────────────────────────────────────────────────────────
    //  Tracking Assist — orbital aim-assist overlay that follows stick input
    // ─────────────────────────────────────────────────────────────────────

    private GamepadState ProcessTrackingAssist(GamepadState state, MacroDefinition macro)
    {
        var runtime = GetRuntime(macro);
        var cfg = macro.TrackingAssist;
        long now = Environment.TickCount64;

        // Trigger gate (same as AimAssistBuff)
        bool isShooting = macro.TriggerSource switch
        {
            TriggerSource.LeftTrigger  => state.LeftTrigger.IsPressed(),
            TriggerSource.RightTrigger => state.RightTrigger.IsPressed(),
            _ => true, // None = always active when activation held
        };

        if (!isShooting)
        {
            runtime.TrackingStartTick = 0;
            return state;
        }

        // Read current stick deflection from the TARGET stick
        double rawX, rawY;
        if (cfg.Target == StickTargetKind.Left)
        {
            rawX = state.LeftStick.X / 32767.0;
            rawY = state.LeftStick.Y / 32767.0;
        }
        else
        {
            rawX = state.RightStick.X / 32767.0;
            rawY = state.RightStick.Y / 32767.0;
        }

        double magnitude = Math.Sqrt(rawX * rawX + rawY * rawY);
        if (magnitude < cfg.DeflectionThreshold)
        {
            runtime.TrackingStartTick = 0;
            return state;
        }

        // Start orbital timer on activation edge
        if (runtime.TrackingStartTick == 0)
            runtime.TrackingStartTick = now;

        // Compute orbital radius: scales with stick deflection via power curve
        double normMag = Math.Clamp((magnitude - cfg.DeflectionThreshold) / (1.0 - cfg.DeflectionThreshold), 0, 1);
        double radiusFactor = Math.Pow(normMag, cfg.ScaleCurve);
        double radius = Lerp(cfg.BaseRadiusNorm, cfg.MaxRadiusNorm, radiusFactor) * cfg.IntensityMul * macro.Intensity;

        // Compute orbital position
        double periodMs = cfg.PeriodMs > 0 ? cfg.PeriodMs : 120;
        double elapsedMs = now - runtime.TrackingStartTick;
        double sign = cfg.Clockwise ? 1.0 : -1.0;
        double theta = sign * (elapsedMs / periodMs) * 2.0 * Math.PI;

        double orbitX = radius * Math.Cos(theta);
        double orbitY = radius * Math.Sin(theta);

        // Always additive — overlay on top of player input
        double scale = 32767.0;
        short dx = (short)Math.Clamp(orbitX * scale, short.MinValue, short.MaxValue);
        short dy = (short)Math.Clamp(orbitY * scale, short.MinValue, short.MaxValue);

        if (cfg.Target == StickTargetKind.Left)
        {
            state.LeftStick = new StickPosition(
                (short)Math.Clamp(state.LeftStick.X + dx, short.MinValue, short.MaxValue),
                (short)Math.Clamp(state.LeftStick.Y + dy, short.MinValue, short.MaxValue));
        }
        else
        {
            state.RightStick = new StickPosition(
                (short)Math.Clamp(state.RightStick.X + dx, short.MinValue, short.MaxValue),
                (short)Math.Clamp(state.RightStick.Y + dy, short.MinValue, short.MaxValue));
        }

        return state;
    }

    /// <summary>
    /// Resolves whether a scripted macro is active right now: respects
    /// toggle-mode, trigger source, and activation button just like NoRecoil.
    /// </summary>
    private static bool IsMacroActive(GamepadState state, MacroDefinition macro, MacroRuntime runtime)
    {
        // Trigger-bound activation takes precedence when TriggerSource is set to a trigger axis.
        bool triggerActive = macro.TriggerSource switch
        {
            TriggerSource.LeftTrigger  => state.LeftTrigger.IsPressed(),
            TriggerSource.RightTrigger => state.RightTrigger.IsPressed(),
            _ => true,  // None → not gated by trigger
        };

        bool buttonActive;
        if (macro.ToggleMode && macro.ActivationButton.HasValue)
        {
            bool pressed = state.IsButtonPressed(macro.ActivationButton.Value);
            if (pressed && !runtime.WasPressed)
                runtime.ToggleState = !runtime.ToggleState;
            runtime.WasPressed = pressed;
            buttonActive = runtime.ToggleState;
        }
        else
        {
            buttonActive = !macro.ActivationButton.HasValue
                || state.IsButtonPressed(macro.ActivationButton.Value);
        }

        return triggerActive && buttonActive;
    }

    /// <summary>Writes a motion sample to the target stick, additive or override per the script.</summary>
    private static void ApplyMotionSample(
        GamepadState state, MotionScript motion, MotionSampler.Sample sample, double macroIntensity)
    {
        double scale = macroIntensity * 32767.0;
        short dx = (short)Math.Clamp(sample.X * scale, short.MinValue, short.MaxValue);
        short dy = (short)Math.Clamp(sample.Y * scale, short.MinValue, short.MaxValue);

        if (motion.Target == StickTargetKind.Left)
        {
            state.LeftStick = motion.Additive
                ? new StickPosition(
                    (short)Math.Clamp(state.LeftStick.X + dx, short.MinValue, short.MaxValue),
                    (short)Math.Clamp(state.LeftStick.Y + dy, short.MinValue, short.MaxValue))
                : new StickPosition(dx, dy);
        }
        else
        {
            state.RightStick = motion.Additive
                ? new StickPosition(
                    (short)Math.Clamp(state.RightStick.X + dx, short.MinValue, short.MaxValue),
                    (short)Math.Clamp(state.RightStick.Y + dy, short.MinValue, short.MaxValue))
                : new StickPosition(dx, dy);
        }
    }

    private sealed class MacroRuntime
    {
        public long LastFireTick;
        public long PulseUntilTick;
        public bool ToggleState;
        public bool WasPressed;
        public int StepIndex;

        // ScriptedShape / HeadAssist — time-parametric state
        public long MotionActivationTick;      // ScriptedShape: start of current motion window
        public long HeadAssistActivationTick;  // HeadAssist: start of current flick
        public long LastHeadAssistTick;        // HeadAssist: cooldown anchor
        public long TriggerDownSinceTick;      // HeadAssist: fire-trigger edge timestamp
        public bool WasFirePressed;            // HeadAssist: fire-trigger edge detector
        public bool WasCycleButtonPressed;     // HeadAssist: manual cycle edge detector
        public DistanceLevel ManualLevel = DistanceLevel.Medium;
        public DistanceLevel CurrentHeadAssistLevel = DistanceLevel.Medium;

        // ProgressiveRecoil — fire-start timestamp
        public long ProgressiveFireStartTick;

        // TrackingAssist — orbital timer
        public long TrackingStartTick;
    }
}
