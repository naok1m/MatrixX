using FluentAssertions;
using InputBusX.Application.Filters;
using InputBusX.Domain.Entities;
using InputBusX.Domain.ValueObjects;
using Xunit;

namespace InputBusX.Tests.Application;

public class CompositeInputFilterTests
{
    private readonly CompositeInputFilter _filter = new();

    [Fact]
    public void Apply_WithDeadzone_ShouldZeroSmallInputs()
    {
        var state = new GamepadState
        {
            LeftStick = new StickPosition(500, 500), // very small input
            RightStick = StickPosition.Zero,
            LeftTrigger = TriggerValue.Zero,
            RightTrigger = TriggerValue.Zero
        };

        var settings = new FilterSettings
        {
            LeftStickDeadzone = 0.1,
            RightStickDeadzone = 0.05
        };

        var result = _filter.Apply(state, settings);

        result.LeftStick.X.Should().Be(0);
        result.LeftStick.Y.Should().Be(0);
    }

    [Fact]
    public void Apply_WithLargeInput_ShouldPassThrough()
    {
        var state = new GamepadState
        {
            LeftStick = new StickPosition(short.MaxValue, 0),
            RightStick = StickPosition.Zero,
            LeftTrigger = TriggerValue.Zero,
            RightTrigger = TriggerValue.Zero
        };

        var settings = new FilterSettings { LeftStickDeadzone = 0.05 };

        var result = _filter.Apply(state, settings);

        result.LeftStick.X.Should().NotBe(0);
    }

    [Fact]
    public void Apply_TriggerDeadzone_ShouldFilterSmallValues()
    {
        var state = new GamepadState
        {
            LeftStick = StickPosition.Zero,
            RightStick = StickPosition.Zero,
            LeftTrigger = new TriggerValue(5), // very small
            RightTrigger = new TriggerValue(200)
        };

        var settings = new FilterSettings { TriggerDeadzone = 0.1 };

        var result = _filter.Apply(state, settings);

        result.LeftTrigger.Value.Should().Be(0);
        result.RightTrigger.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Apply_ResponseCurve_GreaterThanOne_ShouldReduceSensitivity()
    {
        var state = new GamepadState
        {
            LeftStick = new StickPosition((short)(short.MaxValue / 2), 0),
            RightStick = StickPosition.Zero,
            LeftTrigger = TriggerValue.Zero,
            RightTrigger = TriggerValue.Zero
        };

        var linearSettings = new FilterSettings { ResponseCurveExponent = 1.0 };
        var curvedSettings = new FilterSettings { ResponseCurveExponent = 2.0 };

        var linearResult = _filter.Apply(state, linearSettings);
        _filter.Reset();
        var curvedResult = _filter.Apply(state, curvedSettings);

        // With curve > 1, the output for a half-deflection should be less
        Math.Abs(curvedResult.LeftStick.X).Should().BeLessThan(Math.Abs(linearResult.LeftStick.X));
    }

    [Fact]
    public void Reset_ShouldClearSmoothingState()
    {
        var state = new GamepadState
        {
            LeftStick = new StickPosition(short.MaxValue, 0),
            RightStick = StickPosition.Zero,
            LeftTrigger = TriggerValue.Zero,
            RightTrigger = TriggerValue.Zero
        };

        var settings = new FilterSettings { SmoothingEnabled = true, SmoothingFactor = 0.5 };

        _filter.Apply(state, settings);
        _filter.Reset();

        // After reset, next apply should not use previous smoothing state
        var zeroState = GamepadState.Empty;
        var result = _filter.Apply(zeroState, settings);
        result.LeftStick.X.Should().Be(0);
    }
}
