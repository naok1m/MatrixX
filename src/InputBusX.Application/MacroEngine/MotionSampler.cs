using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;

namespace InputBusX.Application.MacroEngine;

/// <summary>
/// Pure sampler for <see cref="MotionScript"/>. Given the script and elapsed
/// time in milliseconds since activation, returns a normalised <c>(x, y)</c>
/// pair in <c>[-1, 1]</c> plus a completion flag. Callers multiply by any
/// intensity scale and cast to <see cref="short"/> before writing to the
/// gamepad state.
///
/// This file has zero allocations on the hot path and no thread-local state —
/// the motion is a pure function of <see cref="MotionScript"/> + time, so the
/// engine can be called from any thread without synchronisation.
/// </summary>
public static class MotionSampler
{
    public readonly record struct Sample(double X, double Y, bool Completed);

    public static Sample Evaluate(MotionScript s, double elapsedMs)
    {
        return s.Shape switch
        {
            ShapeKind.Flick          => SampleFlick(s, elapsedMs),
            ShapeKind.Circle         => SampleCircle(s, elapsedMs),
            ShapeKind.HorizontalOval => SampleOval(s, elapsedMs, s.RadiusXNorm, s.RadiusYNorm, 0),
            ShapeKind.VerticalOval   => SampleOval(s, elapsedMs, s.RadiusYNorm, s.RadiusXNorm, 90),
            ShapeKind.DiagonalOval   => SampleOval(s, elapsedMs, s.RadiusXNorm, s.RadiusYNorm, s.RotationDeg),
            _ => new Sample(0, 0, true),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Flick — one-shot directional pulse with easing on amplitude
    // ─────────────────────────────────────────────────────────────────────

    private static Sample SampleFlick(MotionScript s, double elapsedMs)
    {
        if (s.DurationMs <= 0) return new Sample(0, 0, true);

        double progress = Math.Clamp(elapsedMs / s.DurationMs, 0.0, 1.0);
        bool completed = progress >= 1.0;

        double amp = s.AmplitudeNorm * s.IntensityMul * Ease(s.Easing, 1.0 - progress);
        // 1-progress so amp peaks at t=0 and decays — that is how a natural
        // flick feels: the stick snaps, then the spring pulls it back.

        double rad = s.DirectionDeg * Math.PI / 180.0;
        double x = amp * Math.Cos(rad);
        double y = amp * Math.Sin(rad);
        return new Sample(x, y, completed);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Circle — orbit at constant radius, constant angular velocity
    // ─────────────────────────────────────────────────────────────────────

    private static Sample SampleCircle(MotionScript s, double elapsedMs)
    {
        if (s.PeriodMs <= 0) return new Sample(0, 0, true);

        double theta = PhaseRadians(s, elapsedMs);
        double r = s.RadiusXNorm * s.IntensityMul;
        double x = r * Math.Cos(theta);
        double y = r * Math.Sin(theta);
        bool completed = s.DurationMs > 0 && elapsedMs >= s.DurationMs;
        return new Sample(x, y, completed);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Ellipse (oval) — independent X/Y radii, optional rotation
    // ─────────────────────────────────────────────────────────────────────

    private static Sample SampleOval(MotionScript s, double elapsedMs, double rx, double ry, double rotationDeg)
    {
        if (s.PeriodMs <= 0) return new Sample(0, 0, true);

        double theta = PhaseRadians(s, elapsedMs);
        double lx = rx * s.IntensityMul * Math.Cos(theta);
        double ly = ry * s.IntensityMul * Math.Sin(theta);

        // Rotate by (rotation + the shape's explicit rotation field so DiagonalOval honours it)
        double rotRad = rotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(rotRad);
        double sin = Math.Sin(rotRad);
        double x = lx * cos - ly * sin;
        double y = lx * sin + ly * cos;

        bool completed = s.DurationMs > 0 && elapsedMs >= s.DurationMs;
        return new Sample(x, y, completed);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static double PhaseRadians(MotionScript s, double elapsedMs)
    {
        double sign = s.Clockwise ? 1.0 : -1.0;
        double cyclePhase = (elapsedMs / s.PeriodMs) * 2.0 * Math.PI;
        double startPhase = s.StartPhaseDeg * Math.PI / 180.0;
        return startPhase + sign * cyclePhase;
    }

    private static double Ease(EasingKind kind, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return kind switch
        {
            EasingKind.Linear        => t,
            EasingKind.EaseOutQuad   => 1.0 - (1.0 - t) * (1.0 - t),
            EasingKind.EaseOutCubic  => 1.0 - Math.Pow(1.0 - t, 3.0),
            EasingKind.EaseInOutSine => 0.5 - 0.5 * Math.Cos(Math.PI * t),
            EasingKind.EaseOutBack   => EaseOutBack(t),
            _ => t,
        };
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1.0;
        double x = t - 1.0;
        return 1.0 + c3 * x * x * x + c1 * x * x;
    }
}
