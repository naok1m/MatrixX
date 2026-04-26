using System.Text.Json;
using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;

namespace InputBusX.WebShell;

public sealed class WebShellBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IInputPipeline _pipeline;
    private readonly IInputProvider _inputProvider;
    private readonly IProfileManager _profileManager;

    private readonly object _sync = new();
    private ShellState _state = new();
    private long _lastUiTick;

    public event EventHandler<string>? StateChanged;

    public WebShellBridge(WebShellServices services)
    {
        _pipeline = services.Resolve<IInputPipeline>();
        _inputProvider = services.Resolve<IInputProvider>();
        _profileManager = services.Resolve<IProfileManager>();

        _state = _state with
        {
            ActiveProfileName = _profileManager.ActiveProfile.Name,
            ConnectionStatus = "Disconnected",
            GameProfile = "Warzone"
        };

        _pipeline.RawInputReceived += OnRawInput;
        _pipeline.InputProcessed += OnProcessedInput;
        _inputProvider.DeviceConnected += device => UpdateState(s =>
            s with { ConnectedDevices = s.ConnectedDevices.Append(device.ToString()).Distinct().ToArray() });
        _inputProvider.DeviceDisconnected += device => UpdateState(s =>
            s with { ConnectedDevices = s.ConnectedDevices.Where(d => d != device.ToString()).ToArray() });
        _profileManager.ActiveProfileChanged += profile => UpdateState(s =>
            s with { ActiveProfileName = profile.Name });
    }

    public async Task HandleAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "ready":
            case "getState":
                PublishState();
                break;
            case "toggleConnection":
                await ToggleConnectionAsync();
                break;
            case "setGameProfile":
                if (document.RootElement.TryGetProperty("value", out var value))
                {
                    UpdateState(s => s with { GameProfile = value.GetString() ?? s.GameProfile });
                }
                break;
        }
    }

    private async Task ToggleConnectionAsync()
    {
        ShellState snapshot;
        lock (_sync)
        {
            snapshot = _state;
            _state = _state with
            {
                IsConnecting = true,
                ConnectionStatus = snapshot.IsRunning ? "Stopping..." : "Connecting..."
            };
        }
        PublishState();

        try
        {
            if (snapshot.IsRunning)
            {
                await _pipeline.StopAsync();
                UpdateState(s => s with
                {
                    IsRunning = false,
                    IsConnecting = false,
                    ConnectionStatus = "Disconnected"
                });
                return;
            }

            await _pipeline.StartAsync(CancellationToken.None);
            var status = _pipeline.ViGEmAvailable
                ? _pipeline.VirtualXInputSlot is { } slot
                    ? $"Connected - virtual controller on slot {slot}"
                    : "Connected"
                : "Monitor Only - install ViGEmBus for output";

            UpdateState(s => s with
            {
                IsRunning = true,
                IsConnecting = false,
                ConnectionStatus = status
            });
        }
        catch (Exception ex)
        {
            UpdateState(s => s with
            {
                IsRunning = false,
                IsConnecting = false,
                ConnectionStatus = $"Error: {ex.Message}"
            });
        }
    }

    private void OnRawInput(GamepadState state)
    {
        if (Environment.TickCount64 - _lastUiTick < 16) return;
        _lastUiTick = Environment.TickCount64;

        UpdateState(s => s with
        {
            LeftStickX = state.LeftStick.X / (double)short.MaxValue,
            LeftStickY = state.LeftStick.Y / (double)short.MaxValue,
            RightStickX = state.RightStick.X / (double)short.MaxValue,
            RightStickY = state.RightStick.Y / (double)short.MaxValue,
            LeftTrigger = state.LeftTrigger.Normalized,
            RightTrigger = state.RightTrigger.Normalized,
            RawButtons = ButtonState.From(state.Buttons)
        });
    }

    private void OnProcessedInput(GamepadState state)
    {
        UpdateState(s => s with { OutputButtons = ButtonState.From(state.Buttons) });
    }

    private void UpdateState(Func<ShellState, ShellState> update)
    {
        lock (_sync)
        {
            _state = update(_state);
        }

        PublishState();
    }

    private void PublishState()
    {
        ShellState snapshot;
        lock (_sync)
        {
            snapshot = _state;
        }

        StateChanged?.Invoke(this, JsonSerializer.Serialize(
            new { type = "state", payload = snapshot }, JsonOptions));
    }
}

public sealed record ShellState
{
    public bool IsRunning { get; init; }
    public bool IsConnecting { get; init; }
    public string ConnectionStatus { get; init; } = "Disconnected";
    public string ActiveProfileName { get; init; } = "Default";
    public string GameProfile { get; init; } = "Warzone";
    public string[] ConnectedDevices { get; init; } = [];
    public double LeftStickX { get; init; }
    public double LeftStickY { get; init; }
    public double RightStickX { get; init; }
    public double RightStickY { get; init; }
    public double LeftTrigger { get; init; }
    public double RightTrigger { get; init; }
    public ButtonState RawButtons { get; init; } = new();
    public ButtonState OutputButtons { get; init; } = new();
}

public sealed record ButtonState
{
    public bool A { get; init; }
    public bool B { get; init; }
    public bool X { get; init; }
    public bool Y { get; init; }
    public bool Lb { get; init; }
    public bool Rb { get; init; }
    public bool Start { get; init; }
    public bool Back { get; init; }
    public bool DpadUp { get; init; }
    public bool DpadDown { get; init; }
    public bool DpadLeft { get; init; }
    public bool DpadRight { get; init; }

    public static ButtonState From(GamepadButton buttons) => new()
    {
        A = (buttons & GamepadButton.A) != 0,
        B = (buttons & GamepadButton.B) != 0,
        X = (buttons & GamepadButton.X) != 0,
        Y = (buttons & GamepadButton.Y) != 0,
        Lb = (buttons & GamepadButton.LeftShoulder) != 0,
        Rb = (buttons & GamepadButton.RightShoulder) != 0,
        Start = (buttons & GamepadButton.Start) != 0,
        Back = (buttons & GamepadButton.Back) != 0,
        DpadUp = (buttons & GamepadButton.DPadUp) != 0,
        DpadDown = (buttons & GamepadButton.DPadDown) != 0,
        DpadLeft = (buttons & GamepadButton.DPadLeft) != 0,
        DpadRight = (buttons & GamepadButton.DPadRight) != 0
    };
}
