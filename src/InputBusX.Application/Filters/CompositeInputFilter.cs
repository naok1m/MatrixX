using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using InputBusX.Domain.ValueObjects;

namespace InputBusX.Application.Filters;

public sealed class CompositeInputFilter : IInputFilter
{
    private StickPosition _prevLeftStick;
    private StickPosition _prevRightStick;

    public GamepadState Apply(GamepadState state, FilterSettings settings)
    {
        var result = state.Clone();

        result.LeftStick = ApplyStickFilters(
            result.LeftStick, ref _prevLeftStick,
            settings.LeftStickDeadzone,
            settings.LeftStickAntiDeadzone,
            settings.ResponseCurveExponent,
            settings.SmoothingEnabled ? settings.SmoothingFactor : 0);

        result.RightStick = ApplyStickFilters(
            result.RightStick, ref _prevRightStick,
            settings.RightStickDeadzone,
            settings.RightStickAntiDeadzone,
            settings.ResponseCurveExponent,
            settings.SmoothingEnabled ? settings.SmoothingFactor : 0);

        result.LeftTrigger = ApplyTriggerDeadzone(result.LeftTrigger, settings.TriggerDeadzone);
        result.RightTrigger = ApplyTriggerDeadzone(result.RightTrigger, settings.TriggerDeadzone);

        return result;
    }

    public void Reset()
    {
        _prevLeftStick = StickPosition.Zero;
        _prevRightStick = StickPosition.Zero;
    }

    private static StickPosition ApplyStickFilters(
        StickPosition input,
        ref StickPosition previous,
        double deadzone,
        double antiDeadzone,
        double curveExponent,
        double smoothing)
    {
        double x = input.X / (double)short.MaxValue;
        double y = input.Y / (double)short.MaxValue;
        double magnitude = Math.Sqrt(x * x + y * y);

        if (magnitude < 0.001)
        {
            previous = StickPosition.Zero;
            return StickPosition.Zero;
        }

        double nx = x / magnitude;
        double ny = y / magnitude;

        // Deadzone
        if (magnitude < deadzone)
        {
            previous = StickPosition.Zero;
            return StickPosition.Zero;
        }

        // Remap magnitude from [deadzone, 1] to [0, 1]
        double remapped = (magnitude - deadzone) / (1.0 - deadzone);
        remapped = Math.Min(remapped, 1.0);

        // Anti-deadzone: remap from [0, 1] to [antiDeadzone, 1]
        if (antiDeadzone > 0)
            remapped = antiDeadzone + remapped * (1.0 - antiDeadzone);

        // Response curve
        if (Math.Abs(curveExponent - 1.0) > 0.001)
            remapped = Math.Pow(remapped, curveExponent);

        double outX = nx * remapped;
        double outY = ny * remapped;

        // Smoothing (exponential moving average)
        if (smoothing > 0.001)
        {
            double prevX = previous.X / (double)short.MaxValue;
            double prevY = previous.Y / (double)short.MaxValue;
            outX = prevX + (outX - prevX) * (1.0 - smoothing);
            outY = prevY + (outY - prevY) * (1.0 - smoothing);
        }

        var result = new StickPosition(
            (short)Math.Clamp(outX * short.MaxValue, short.MinValue, short.MaxValue),
            (short)Math.Clamp(outY * short.MaxValue, short.MinValue, short.MaxValue));

        previous = result;
        return result;
    }

    private static TriggerValue ApplyTriggerDeadzone(TriggerValue trigger, double deadzone)
    {
        double normalized = trigger.Normalized;
        if (normalized < deadzone)
            return TriggerValue.Zero;

        double remapped = (normalized - deadzone) / (1.0 - deadzone);
        return new TriggerValue((byte)(remapped * 255));
    }
}
