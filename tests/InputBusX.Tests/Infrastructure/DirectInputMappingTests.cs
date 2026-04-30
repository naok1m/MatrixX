using FluentAssertions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Infrastructure.Input;
using Xunit;

namespace InputBusX.Tests.Infrastructure;

public class DirectInputMappingTests
{
    [Fact]
    public void DualShock4_Profile_ShouldMapFaceButtonsAndSticks()
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x05C4, "Wireless Controller"));
        var rest = Snapshot();
        var pressed = Snapshot(
            x: 65535,
            y: 0,
            buttons: Buttons(1),
            pov: 9000);

        var state = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), pressed);

        profile.IsExplicit.Should().BeTrue();
        profile.Name.Should().Contain("DualShock 4");
        state.Buttons.Should().HaveFlag(GamepadButton.A);
        state.Buttons.Should().HaveFlag(GamepadButton.DPadRight);
        state.LeftStick.X.Should().BeGreaterThan(30000);
        state.LeftStick.Y.Should().BeGreaterThan(30000);
    }

    [Fact]
    public void DualSense_Profile_ShouldMapModernRightStickAndTriggers()
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x0CE6, "DualSense Wireless Controller"));
        var rest = Snapshot(rx: 32767, ry: 32767, rz: 32767);
        var pressed = Snapshot(z: 65535, rz: 0, rx: 65535, ry: 65535);

        var state = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), pressed);

        profile.IsExplicit.Should().BeTrue();
        profile.Name.Should().Contain("DualSense");
        state.RightStick.X.Should().BeGreaterThan(30000);
        state.RightStick.Y.Should().BeGreaterThan(30000);
        state.LeftTrigger.Value.Should().Be(255);
        state.RightTrigger.Value.Should().Be(255);
    }

    [Fact]
    public void DualSense_RightStickDown_ShouldNotTriggerR2()
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x0CE6, "DualSense Wireless Controller"));
        var rest = Snapshot(rx: 32767, ry: 32767, rz: 32767);
        var rightStickDown = Snapshot(rz: 65535, rx: 32767, ry: 32767);

        var state = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), rightStickDown);

        state.RightStick.Y.Should().BeLessThan(-30000);
        state.RightTrigger.Value.Should().Be(0);
    }

    [Fact]
    public void DualSense_TriggerButtons_ShouldDriveTriggersWhenAxesDoNotMove()
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x0CE6, "DualSense Wireless Controller"));
        var rest = Snapshot(rx: 32767, ry: 32767, rz: 32767);
        var pressed = Snapshot(rx: 32767, ry: 32767, rz: 32767, buttons: Buttons(6, 7));

        var state = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), pressed);

        state.LeftTrigger.Value.Should().Be(255);
        state.RightTrigger.Value.Should().Be(255);
    }

    [Fact]
    public void DualSense_TriggerSliders_ShouldDriveTriggersWhenDriverUsesSliders()
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x0CE6, "DualSense Wireless Controller"));
        var rest = Snapshot(rx: 32767, ry: 32767, rz: 32767, lt: 32767, rt: 32767);
        var pressed = Snapshot(rx: 32767, ry: 32767, rz: 32767, lt: 65535, rt: 65535);

        var state = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), pressed);

        state.LeftTrigger.Value.Should().Be(255);
        state.RightTrigger.Value.Should().Be(255);
    }

    [Theory]
    [InlineData(0, 65535)]
    [InlineData(32767, 65535)]
    [InlineData(65535, 0)]
    public void TriggerRestCalibration_ShouldTreatRestAsNeutral(int restValue, int pressedValue)
    {
        var profile = DirectInputProfile.For(Identity(0x054C, 0x0CE6, "DualSense Wireless Controller"));
        var rest = Snapshot(rx: restValue, ry: restValue);
        var atRest = DirectInputMappingSession.MapAfterCalibration(profile, Repeat(rest), rest);
        var pressed = DirectInputMappingSession.MapAfterCalibration(
            profile,
            Repeat(rest),
            Snapshot(rx: pressedValue, ry: pressedValue));

        atRest.LeftTrigger.Value.Should().Be(0);
        atRest.RightTrigger.Value.Should().Be(0);
        pressed.LeftTrigger.Value.Should().BeGreaterThan(240);
        pressed.RightTrigger.Value.Should().BeGreaterThan(240);
    }

    [Fact]
    public void Deduplicator_ShouldSuppressDirectInputWhenPhysicalIdentityMatchesXInput()
    {
        var deduplicator = new InputDeviceDeduplicator();
        var xinput = Device("xinput_0", DeviceType.XInput, "hid:vid_045e&pid_02ff:path");
        var dinput = Device("dinput_a", DeviceType.DirectInput, "hid:vid_045e&pid_02ff:path");

        deduplicator.Connect(xinput).Should().BeTrue();
        deduplicator.Connect(dinput).Should().BeFalse();

        deduplicator.ShouldPublish("xinput_0").Should().BeTrue();
        deduplicator.ShouldPublish("dinput_a").Should().BeFalse();
        deduplicator.FilterConnected([xinput, dinput]).Should().ContainSingle(d => d.Id == "xinput_0");
    }

    [Fact]
    public void Deduplicator_ShouldAllowXboxXInputAndDualSenseDirectInputTogether()
    {
        var deduplicator = new InputDeviceDeduplicator();
        var xinput = Device("xinput_0", DeviceType.XInput, "xinput:slot:0");
        var dualSense = Device(
            "dinput_ds5",
            DeviceType.DirectInput,
            "hid:vid_054c&pid_0ce6:instance_ds5",
            vendorId: 0x054C,
            productId: 0x0CE6);

        deduplicator.Connect(xinput).Should().BeTrue();
        deduplicator.Connect(dualSense).Should().BeTrue();

        deduplicator.ShouldPublish("xinput_0").Should().BeTrue();
        deduplicator.ShouldPublish("dinput_ds5").Should().BeTrue();
        deduplicator.FilterConnected([xinput, dualSense]).Should().HaveCount(2);
    }

    private static DirectInputDeviceIdentity Identity(ushort vendorId, ushort productId, string name)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(vendorId).CopyTo(bytes, 0);
        BitConverter.GetBytes(productId).CopyTo(bytes, 2);
        return DirectInputDeviceIdentity.From(new Guid(bytes), Guid.NewGuid(), name);
    }

    private static IEnumerable<DirectInputSnapshot> Repeat(DirectInputSnapshot snapshot) =>
        Enumerable.Repeat(snapshot, 4);

    private static bool[] Buttons(params int[] pressed)
    {
        var buttons = new bool[14];
        foreach (var index in pressed)
            buttons[index] = true;

        return buttons;
    }

    private static DirectInputSnapshot Snapshot(
        int x = 32767,
        int y = 32767,
        int z = 32767,
        int rx = 32767,
        int ry = 0,
        int rz = 0,
        int lt = 0,
        int rt = 0,
        IReadOnlyList<bool>? buttons = null,
        int pov = -1) =>
        new(x, y, z, rx, ry, rz, [lt, rt], buttons ?? Buttons(), [pov]);

    private static InputDevice Device(
        string id,
        DeviceType type,
        string physicalIdentity,
        ushort? vendorId = null,
        ushort? productId = null) =>
        new()
        {
            Id = id,
            Name = id,
            Type = type,
            PlayerIndex = 0,
            IsConnected = true,
            LastSeen = DateTime.UtcNow,
            PhysicalIdentity = physicalIdentity,
            VendorId = vendorId,
            ProductId = productId
        };
}
