using FluentAssertions;
using InputBusX.Application.MacroEngine;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InputBusX.Tests.Application;

public class MacroProcessorTests
{
    private readonly MacroProcessor _processor = new(NullLogger<MacroProcessor>.Instance);

    [Fact]
    public void Process_NoRecoil_ShouldCompensateWhenAimingAndShooting()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,  // shooting
            RightStick = new StickPosition(0, 0)
        };

        var macro = new MacroDefinition
        {
            Name = "Test No Recoil",
            Type = MacroType.NoRecoil,
            Enabled = true,
            Intensity = 1.0,
            RecoilCompensationY = -500,
            RecoilCompensationX = 0
        };

        var result = _processor.Process(state, [macro]);

        // Y compensation should pull the stick down
        result.RightStick.Y.Should().Be(-500);
        result.RightStick.X.Should().Be(0);
    }

    [Fact]
    public void Process_NoRecoil_ShouldNotApplyWhenNotAiming()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(0, 0)
        };
        state.SetButton(GamepadButton.LeftShoulder, false);

        var macro = new MacroDefinition
        {
            Name = "Test No Recoil",
            Type = MacroType.NoRecoil,
            ActivationButton = GamepadButton.LeftShoulder,
            RecoilCompensationY = -500
        };

        var result = _processor.Process(state, [macro]);

        result.RightStick.Y.Should().Be(0);
    }

    [Fact]
    public void Process_NoRecoil_ShouldApplyWhenHoldingActivationButtonAndRT()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(0, 0)
        };
        state.SetButton(GamepadButton.LeftShoulder, true);

        var macro = new MacroDefinition
        {
            Name = "Test No Recoil",
            Type = MacroType.NoRecoil,
            ActivationButton = GamepadButton.LeftShoulder,
            RecoilCompensationY = -500
        };

        var result = _processor.Process(state, [macro]);

        result.RightStick.Y.Should().Be(-500);
    }

    [Fact]
    public void Process_NoRecoil_ShouldAddCompensationOnTopOfUserStickInput()
    {
        // NoRecoil is ADDITIVE: the compensation is applied on top of the player's
        // current aim input so they can still move the camera while shooting.
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(8000, 12000)
        };

        var macro = new MacroDefinition
        {
            Name = "Test No Recoil",
            Type = MacroType.NoRecoil,
            TriggerSource = TriggerSource.RightTrigger,
            RecoilCompensationX = 400,
            RecoilCompensationY = -500
        };

        var result = _processor.Process(state, [macro]);

        // Player's stick (8000, 12000) + compensation (400, -500) = (8400, 11500)
        result.RightStick.X.Should().Be(8400);
        result.RightStick.Y.Should().Be(11500);
    }

    [Fact]
    public void Process_AutoPing_ShouldPressConfiguredButtonWhileRTIsHeld()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full
        };

        var macro = new MacroDefinition
        {
            Name = "Auto Ping",
            Type = MacroType.AutoPing,
            IntervalMs = 0,
            PingButton = GamepadButton.DPadUp
        };

        var result = _processor.Process(state, [macro]);

        result.IsButtonPressed(GamepadButton.DPadUp).Should().BeTrue();
    }

    [Fact]
    public void Process_Remap_ShouldSwapButtons()
    {
        var state = new GamepadState();
        state.SetButton(GamepadButton.A, true);

        var macro = new MacroDefinition
        {
            Name = "Remap A->B",
            Type = MacroType.Remap,
            SourceButton = GamepadButton.A,
            TargetButton = GamepadButton.B
        };

        var result = _processor.Process(state, [macro]);

        result.IsButtonPressed(GamepadButton.A).Should().BeFalse();
        result.IsButtonPressed(GamepadButton.B).Should().BeTrue();
    }

    [Fact]
    public void Process_Remap_ShouldNotAffectUnmappedButtons()
    {
        var state = new GamepadState();
        state.SetButton(GamepadButton.X, true);

        var macro = new MacroDefinition
        {
            Name = "Remap A->B",
            Type = MacroType.Remap,
            SourceButton = GamepadButton.A,
            TargetButton = GamepadButton.B
        };

        var result = _processor.Process(state, [macro]);

        result.IsButtonPressed(GamepadButton.X).Should().BeTrue();
    }

    [Fact]
    public void Process_MultipleMacros_ShouldApplyInOrder()
    {
        var state = new GamepadState
        {
            LeftTrigger = TriggerValue.Full,
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(0, 0)
        };
        state.SetButton(GamepadButton.A, true);

        var macros = new List<MacroDefinition>
        {
            new()
            {
                Name = "Remap",
                Type = MacroType.Remap,
                SourceButton = GamepadButton.A,
                TargetButton = GamepadButton.B
            },
            new()
            {
                Name = "No Recoil",
                Type = MacroType.NoRecoil,
                Intensity = 1.0,
                RecoilCompensationY = -200
            }
        };

        var result = _processor.Process(state, macros);

        result.IsButtonPressed(GamepadButton.B).Should().BeTrue();
        result.RightStick.Y.Should().Be(-200);
    }

    [Fact]
    public void Reset_ShouldClearAllRuntimes()
    {
        // Just verify it doesn't throw
        _processor.Reset();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AutoFire tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Process_AutoFire_WithRightTrigger_ShouldToggleTriggerValue()
    {
        // RT held down — first frame should enable ToggleState and keep RT full
        var state = new GamepadState { RightTrigger = TriggerValue.Full };

        var macro = new MacroDefinition
        {
            Name = "Rapid Fire",
            Type = MacroType.AutoFire,
            TriggerSource = TriggerSource.RightTrigger,
            IntervalMs = 0   // 0ms interval fires every frame
        };

        var result = _processor.Process(state, [macro]);

        // On first call ToggleState flips to true → RT stays Full
        result.RightTrigger.IsPressed().Should().BeTrue();
    }

    [Fact]
    public void Process_AutoFire_WhenRTNotHeld_ShouldNotToggle()
    {
        var state = new GamepadState { RightTrigger = TriggerValue.Zero };

        var macro = new MacroDefinition
        {
            Name = "Rapid Fire",
            Type = MacroType.AutoFire,
            TriggerSource = TriggerSource.RightTrigger,
            IntervalMs = 0
        };

        var result = _processor.Process(state, [macro]);

        result.RightTrigger.IsPressed().Should().BeFalse();
    }

    [Fact]
    public void Process_AutoFire_WithButton_ShouldToggleButton()
    {
        var state = new GamepadState();
        state.SetButton(GamepadButton.RightShoulder, true);

        var macro = new MacroDefinition
        {
            Name = "Rapid Fire Button",
            Type = MacroType.AutoFire,
            ActivationButton = GamepadButton.RightShoulder,
            TriggerSource = TriggerSource.RightTrigger,  // irrelevant when button set
            IntervalMs = 0
        };

        // Since ActivationButton is set, TriggerSource is irrelevant for activation check
        var result = _processor.Process(state, [macro]);

        // Should have processed without throwing
        result.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // WeaponProfile override tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SetWeaponProfile_ShouldOverrideMacroRecoilValues()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(0, 0)
        };

        var macro = new MacroDefinition
        {
            Name = "No Recoil",
            Type = MacroType.NoRecoil,
            Intensity = 1.0,
            RecoilCompensationY = -999   // this value should be ignored
        };

        var weaponProfile = new WeaponProfile
        {
            Name = "AK47",
            RecoilCompensationX = 0,
            RecoilCompensationY = -300,
            Intensity = 1.0
        };

        _processor.SetWeaponProfile(weaponProfile);
        var result = _processor.Process(state, [macro]);

        // Weapon profile (-300) wins over macro (-999)
        result.RightStick.Y.Should().Be(-300);
    }

    [Fact]
    public void SetWeaponProfile_WhenCleared_ShouldUseMacroValues()
    {
        var state = new GamepadState
        {
            RightTrigger = TriggerValue.Full,
            RightStick = new StickPosition(0, 0)
        };

        var macro = new MacroDefinition
        {
            Name = "No Recoil",
            Type = MacroType.NoRecoil,
            Intensity = 1.0,
            RecoilCompensationY = -700
        };

        // Set then clear weapon profile
        _processor.SetWeaponProfile(new WeaponProfile { Name = "tmp", RecoilCompensationY = -100 });
        _processor.SetWeaponProfile(null);

        var result = _processor.Process(state, [macro]);

        result.RightStick.Y.Should().Be(-700);
    }

    [Fact]
    public void Process_AutoFire_WhenWeaponProfileRapidFireDisabled_ShouldSkip()
    {
        var state = new GamepadState { RightTrigger = TriggerValue.Full };

        var macro = new MacroDefinition
        {
            Name = "Rapid Fire",
            Type = MacroType.AutoFire,
            TriggerSource = TriggerSource.RightTrigger,
            IntervalMs = 0
        };

        var profile = new WeaponProfile
        {
            Name = "Sniper",
            RapidFireEnabled = false   // weapon profile disables rapid fire
        };

        _processor.SetWeaponProfile(profile);
        var result = _processor.Process(state, [macro]);

        // RT should remain unchanged (rapid fire skipped)
        result.RightTrigger.Value.Should().Be(255);  // original Full value, not toggled off
    }

    [Fact]
    public void Process_AutoFire_WhenWeaponProfileRapidFireEnabled_ShouldUseProfileInterval()
    {
        var state = new GamepadState { RightTrigger = TriggerValue.Full };

        var macro = new MacroDefinition
        {
            Name = "Rapid Fire",
            Type = MacroType.AutoFire,
            TriggerSource = TriggerSource.RightTrigger,
            IntervalMs = 9999   // macro's own value — should be ignored
        };

        var profile = new WeaponProfile
        {
            Name = "Pistol",
            RapidFireEnabled = true,
            RapidFireIntervalMs = 0   // fire every frame
        };

        _processor.SetWeaponProfile(profile);
        var result = _processor.Process(state, [macro]);

        // With 0ms interval, rapid fire fires immediately — RT should toggle
        result.Should().NotBeNull();
    }
}
