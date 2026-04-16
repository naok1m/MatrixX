using FluentAssertions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.ValueObjects;
using Xunit;

namespace InputBusX.Tests.Domain;

public class GamepadStateTests
{
    [Fact]
    public void SetButton_ShouldSetAndUnsetButtons()
    {
        var state = GamepadState.Empty;

        state.SetButton(GamepadButton.A, true);
        state.IsButtonPressed(GamepadButton.A).Should().BeTrue();
        state.IsButtonPressed(GamepadButton.B).Should().BeFalse();

        state.SetButton(GamepadButton.A, false);
        state.IsButtonPressed(GamepadButton.A).Should().BeFalse();
    }

    [Fact]
    public void SetButton_MultipleButtons_ShouldTrackIndependently()
    {
        var state = GamepadState.Empty;

        state.SetButton(GamepadButton.A, true);
        state.SetButton(GamepadButton.B, true);
        state.SetButton(GamepadButton.X, true);

        state.IsButtonPressed(GamepadButton.A).Should().BeTrue();
        state.IsButtonPressed(GamepadButton.B).Should().BeTrue();
        state.IsButtonPressed(GamepadButton.X).Should().BeTrue();
        state.IsButtonPressed(GamepadButton.Y).Should().BeFalse();

        state.SetButton(GamepadButton.B, false);
        state.IsButtonPressed(GamepadButton.A).Should().BeTrue();
        state.IsButtonPressed(GamepadButton.B).Should().BeFalse();
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        var original = new GamepadState
        {
            Buttons = GamepadButton.A | GamepadButton.B,
            LeftStick = new StickPosition(1000, -2000),
            RightStick = new StickPosition(-500, 500),
            LeftTrigger = new TriggerValue(128),
            RightTrigger = new TriggerValue(255)
        };

        var clone = original.Clone();

        clone.Buttons.Should().Be(original.Buttons);
        clone.LeftStick.Should().Be(original.LeftStick);
        clone.LeftTrigger.Should().Be(original.LeftTrigger);

        // Mutating clone should not affect original
        clone.SetButton(GamepadButton.A, false);
        original.IsButtonPressed(GamepadButton.A).Should().BeTrue();
    }

    [Fact]
    public void StickPosition_Magnitude_ShouldCalculateCorrectly()
    {
        var center = StickPosition.Zero;
        center.Magnitude.Should().Be(0);

        var maxRight = new StickPosition(short.MaxValue, 0);
        maxRight.NormalizedMagnitude.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void StickPosition_WithDeadzone_ShouldZeroOutBelowThreshold()
    {
        var small = new StickPosition(100, 100);
        var result = small.WithDeadzone(0.5);
        result.Should().Be(StickPosition.Zero);

        var large = new StickPosition(short.MaxValue, 0);
        var result2 = large.WithDeadzone(0.5);
        result2.Should().NotBe(StickPosition.Zero);
    }

    [Fact]
    public void TriggerValue_IsPressed_ShouldRespectThreshold()
    {
        var zero = TriggerValue.Zero;
        zero.IsPressed().Should().BeFalse();

        var full = TriggerValue.Full;
        full.IsPressed().Should().BeTrue();

        var mid = new TriggerValue(30);
        mid.IsPressed(30).Should().BeTrue();
        mid.IsPressed(31).Should().BeFalse();
    }
}
