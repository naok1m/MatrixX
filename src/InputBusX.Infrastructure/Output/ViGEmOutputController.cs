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

    // Last submitted state — skip SubmitReport when nothing changed
    private GamepadButton _lastButtons;
    private short _lastLX, _lastLY, _lastRX, _lastRY;
    private byte _lastLT, _lastRT;

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

            // UserIndex is assigned by ViGEmBus asynchronously after Connect().
            // Poll briefly — the bus typically responds within a few ms.
            // If it doesn't report in time, continue without a known slot
            // (virtual output still works; XInput exclusion is just best-effort).
            _virtualSlot = TryGetUserIndex(_controller);

            if (_virtualSlot.HasValue)
                _logger.LogInformation(
                    "ViGEm virtual Xbox 360 controller connected on XInput slot {Slot}", _virtualSlot);
            else
                _logger.LogInformation(
                    "ViGEm virtual Xbox 360 controller connected (slot not yet reported by ViGEmBus)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ViGEm controller. Ensure ViGEmBus is installed");
            Disconnect();
            throw;
        }
    }

    /// <summary>
    /// Polls UserIndex up to ~200 ms after Connect(). ViGEmBus assigns the slot
    /// asynchronously; accessing it too early throws Xbox360UserIndexNotReportedException.
    /// Returns null if the slot is still not available after the timeout.
    /// </summary>
    private static int? TryGetUserIndex(IXbox360Controller controller)
    {
        const int maxAttempts = 20;
        const int delayMs     = 10;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                return (int)controller.UserIndex;
            }
            catch (Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions.Xbox360UserIndexNotReportedException)
            {
                Thread.Sleep(delayMs);
            }
        }

        return null;
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

        var buttons = state.Buttons;
        short lx = state.LeftStick.X,  ly = state.LeftStick.Y;
        short rx = state.RightStick.X, ry = state.RightStick.Y;
        byte  lt = state.LeftTrigger.Value, rt = state.RightTrigger.Value;

        // Only push a HID report when something actually changed.
        // SubmitReport() is a kernel call (~0.5–2 ms); skipping it when idle
        // eliminates constant USB bus traffic and frees up the game's XInput polling.
        if (buttons == _lastButtons &&
            lx == _lastLX && ly == _lastLY &&
            rx == _lastRX && ry == _lastRY &&
            lt == _lastLT && rt == _lastRT)
            return;

        _lastButtons = buttons;
        _lastLX = lx; _lastLY = ly;
        _lastRX = rx; _lastRY = ry;
        _lastLT = lt; _lastRT = rt;

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

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX,  lx);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY,  ly);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, rx);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, ry);

        _controller.SetSliderValue(Xbox360Slider.LeftTrigger,  lt);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, rt);

        _controller.SubmitReport();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
