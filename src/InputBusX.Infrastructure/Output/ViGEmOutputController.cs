using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace InputBusX.Infrastructure.Output;

public sealed class ViGEmOutputController : IOutputController
{
    private readonly ILogger<ViGEmOutputController> _logger;
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private int? _virtualSlot;

    public bool IsConnected => _controller is not null;
    public int? VirtualXInputSlot => _virtualSlot;

    public ViGEmOutputController(ILogger<ViGEmOutputController> logger)
    {
        _logger = logger;
    }

    public void Connect()
    {
        if (_controller is not null) return;

        try
        {
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();

            // UserIndex is set by ViGEmBus after Connect() — tells us which XInput
            // slot (0-3) the virtual controller was assigned so we can exclude it
            // from physical input polling and prevent a feedback loop.
            _virtualSlot = (int)_controller.UserIndex;
            _logger.LogInformation(
                "ViGEm virtual Xbox 360 controller connected on XInput slot {Slot}", _virtualSlot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ViGEm controller. Ensure ViGEmBus is installed");
            Disconnect();
            throw;
        }
    }

    public void Disconnect()
    {
        try
        {
            _controller?.Disconnect();
        }
        catch { }

        _controller = null;
        _virtualSlot = null;
        _client?.Dispose();
        _client = null;
        _logger.LogInformation("ViGEm controller disconnected");
    }

    public void Update(GamepadState state)
    {
        if (_controller is null) return;

        _controller.SetButtonState(Xbox360Button.A,            state.IsButtonPressed(GamepadButton.A));
        _controller.SetButtonState(Xbox360Button.B,            state.IsButtonPressed(GamepadButton.B));
        _controller.SetButtonState(Xbox360Button.X,            state.IsButtonPressed(GamepadButton.X));
        _controller.SetButtonState(Xbox360Button.Y,            state.IsButtonPressed(GamepadButton.Y));
        _controller.SetButtonState(Xbox360Button.Start,        state.IsButtonPressed(GamepadButton.Start));
        _controller.SetButtonState(Xbox360Button.Back,         state.IsButtonPressed(GamepadButton.Back));
        _controller.SetButtonState(Xbox360Button.LeftShoulder, state.IsButtonPressed(GamepadButton.LeftShoulder));
        _controller.SetButtonState(Xbox360Button.RightShoulder,state.IsButtonPressed(GamepadButton.RightShoulder));
        _controller.SetButtonState(Xbox360Button.LeftThumb,    state.IsButtonPressed(GamepadButton.LeftThumb));
        _controller.SetButtonState(Xbox360Button.RightThumb,   state.IsButtonPressed(GamepadButton.RightThumb));
        _controller.SetButtonState(Xbox360Button.Guide,        state.IsButtonPressed(GamepadButton.Guide));
        _controller.SetButtonState(Xbox360Button.Up,           state.IsButtonPressed(GamepadButton.DPadUp));
        _controller.SetButtonState(Xbox360Button.Down,         state.IsButtonPressed(GamepadButton.DPadDown));
        _controller.SetButtonState(Xbox360Button.Left,         state.IsButtonPressed(GamepadButton.DPadLeft));
        _controller.SetButtonState(Xbox360Button.Right,        state.IsButtonPressed(GamepadButton.DPadRight));

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX,  state.LeftStick.X);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY,  state.LeftStick.Y);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightStick.X);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightStick.Y);

        _controller.SetSliderValue(Xbox360Slider.LeftTrigger,  state.LeftTrigger.Value);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger.Value);

        _controller.SubmitReport();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
