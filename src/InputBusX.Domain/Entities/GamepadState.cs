using InputBusX.Domain.Enums;
using InputBusX.Domain.ValueObjects;

namespace InputBusX.Domain.Entities;

public sealed class GamepadState
{
    public GamepadButton Buttons { get; set; }
    public StickPosition LeftStick { get; set; }
    public StickPosition RightStick { get; set; }
    public TriggerValue LeftTrigger { get; set; }
    public TriggerValue RightTrigger { get; set; }
    public long TimestampTicks { get; set; }

    public bool IsButtonPressed(GamepadButton button) => (Buttons & button) != 0;

    public void SetButton(GamepadButton button, bool pressed)
    {
        if (pressed)
            Buttons |= button;
        else
            Buttons &= ~button;
    }

    public GamepadState Clone() => new()
    {
        Buttons = Buttons,
        LeftStick = LeftStick,
        RightStick = RightStick,
        LeftTrigger = LeftTrigger,
        RightTrigger = RightTrigger,
        TimestampTicks = TimestampTicks
    };

    public static GamepadState Empty => new()
    {
        TimestampTicks = Environment.TickCount64
    };
}
