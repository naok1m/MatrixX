using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.UI.ViewModels;

/// <summary>Per-controller row in the HidHide card.</summary>
public partial class ControllerRowViewModel : ViewModelBase
{
    public string InstanceId   { get; }
    public string FriendlyName { get; }

    [ObservableProperty] private bool _isHidden;

    public ControllerRowViewModel(string instanceId, string friendlyName, bool isHidden)
    {
        InstanceId   = instanceId;
        FriendlyName = friendlyName;
        _isHidden    = isHidden;
    }
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IInputPipeline _pipeline;
    private readonly IInputProvider _inputProvider;
    private readonly IProfileManager _profileManager;
    private readonly IHidHideService? _hidHide;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _activeProfileName = "Default";

    // Left stick
    [ObservableProperty] private double _leftStickX;
    [ObservableProperty] private double _leftStickY;

    // Right stick
    [ObservableProperty] private double _rightStickX;
    [ObservableProperty] private double _rightStickY;

    // Triggers
    [ObservableProperty] private double _leftTrigger;
    [ObservableProperty] private double _rightTrigger;

    // Buttons (raw input — from physical controller)
    [ObservableProperty] private bool _buttonA;
    [ObservableProperty] private bool _buttonB;
    [ObservableProperty] private bool _buttonX;
    [ObservableProperty] private bool _buttonY;
    [ObservableProperty] private bool _buttonLB;
    [ObservableProperty] private bool _buttonRB;
    [ObservableProperty] private bool _buttonStart;
    [ObservableProperty] private bool _buttonBack;
    [ObservableProperty] private bool _dpadUp;
    [ObservableProperty] private bool _dpadDown;
    [ObservableProperty] private bool _dpadLeft;
    [ObservableProperty] private bool _dpadRight;

    // Processed output — what actually gets sent to the virtual controller / game
    [ObservableProperty] private bool _outDpadUp;
    [ObservableProperty] private bool _outDpadDown;
    [ObservableProperty] private bool _outDpadLeft;
    [ObservableProperty] private bool _outDpadRight;
    [ObservableProperty] private bool _outButtonA;
    [ObservableProperty] private bool _outButtonB;
    [ObservableProperty] private bool _outButtonX;
    [ObservableProperty] private bool _outButtonY;

    // HidHide
    [ObservableProperty] private bool _isHidHideAvailable;
    [ObservableProperty] private string _hideAllStatusText = "Hide All";

    public ObservableCollection<string> ConnectedDevices { get; } = [];
    public ObservableCollection<ControllerRowViewModel> Controllers { get; } = [];

    // Throttle UI updates to ~60 Hz — the XInput poll loop runs at up to 1000 Hz,
    // posting every frame to the UI thread causes unnecessary load.
    private const long UiFrameMs = 16; // ≈60 fps
    private long _lastRawUiTick;
    private long _lastProcessedUiTick;

    public DashboardViewModel(
        IInputPipeline pipeline,
        IInputProvider inputProvider,
        IProfileManager profileManager,
        IHidHideService? hidHide,
        ILogger<DashboardViewModel> logger)
    {
        _pipeline = pipeline;
        _inputProvider = inputProvider;
        _profileManager = profileManager;
        _hidHide = hidHide;
        _logger = logger;

        _pipeline.RawInputReceived += OnRawInput;
        _pipeline.InputProcessed += OnProcessedInput;

        _inputProvider.DeviceConnected += d => OnDeviceChange(d, true);
        _inputProvider.DeviceDisconnected += d => OnDeviceChange(d, false);
        _profileManager.ActiveProfileChanged += p => ActiveProfileName = p.Name;
        ActiveProfileName = _profileManager.ActiveProfile.Name;

        IsHidHideAvailable = _hidHide?.IsAvailable ?? false;
        if (IsHidHideAvailable) RefreshControllers();
    }

    [RelayCommand]
    private async Task ToggleConnection()
    {
        if (IsConnecting) return;

        IsConnecting = true;
        ConnectionStatus = IsRunning ? "Stopping..." : "Connecting...";

        try
        {
            if (IsRunning)
            {
                await _pipeline.StopAsync();
                IsRunning = false;
                ConnectionStatus = "Disconnected";
            }
            else
            {
                await _pipeline.StartAsync(CancellationToken.None);
                IsRunning = true;
                ConnectionStatus = _pipeline.ViGEmAvailable
                    ? "Connected"
                    : "Monitor Only — install ViGEmBus for output";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle connection");
            ConnectionStatus = $"Error: {ex.Message}";
            IsRunning = false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    // ── HidHide commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshControllers()
    {
        if (_hidHide == null) return;
        try
        {
            Controllers.Clear();
            foreach (var c in _hidHide.GetControllers())
                Controllers.Add(new ControllerRowViewModel(c.InstanceId, c.FriendlyName, c.IsHidden));

            UpdateHideAllStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh HidHide controller list");
        }
    }

    [RelayCommand]
    private void ToggleHideDevice(ControllerRowViewModel? row)
    {
        if (row == null || _hidHide == null) return;
        try
        {
            if (row.IsHidden)
            {
                _hidHide.UnhideDevice(row.InstanceId);
                row.IsHidden = false;
            }
            else
            {
                _hidHide.HideDevice(row.InstanceId);
                row.IsHidden = true;
            }
            UpdateHideAllStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle hide for {InstanceId}", row.InstanceId);
        }
    }

    [RelayCommand]
    private void HideAll()
    {
        if (_hidHide == null) return;
        try
        {
            _hidHide.HidePhysicalControllers();
            foreach (var row in Controllers) row.IsHidden = true;
            HideAllStatusText = "All Hidden";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hide all controllers");
        }
    }

    [RelayCommand]
    private void UnhideAll()
    {
        if (_hidHide == null) return;
        try
        {
            _hidHide.UnhidePhysicalControllers();
            foreach (var row in Controllers) row.IsHidden = false;
            HideAllStatusText = "Hide All";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unhide all controllers");
        }
    }

    private void UpdateHideAllStatus()
    {
        HideAllStatusText = Controllers.All(c => c.IsHidden) ? "All Hidden" :
                            Controllers.Any(c => c.IsHidden) ? "Partially Hidden" :
                            "Hide All";
    }

    // ── Input event handlers ──────────────────────────────────────────────────

    private void OnRawInput(GamepadState state)
    {
        // Throttle to ~60 Hz: at 1000 Hz polling, ~984 out of 1000 frames are dropped here.
        // The macro pipeline still processes every frame at full speed — only the visualizer is throttled.
        var now = Environment.TickCount64;
        if (now - _lastRawUiTick < UiFrameMs) return;
        _lastRawUiTick = now;

        // Capture values before crossing thread boundary
        var lx = state.LeftStick.X / (double)short.MaxValue;
        var ly = state.LeftStick.Y / (double)short.MaxValue;
        var rx = state.RightStick.X / (double)short.MaxValue;
        var ry = state.RightStick.Y / (double)short.MaxValue;
        var lt = state.LeftTrigger.Normalized;
        var rt = state.RightTrigger.Normalized;
        var btns = state.Buttons;

        Dispatcher.UIThread.Post(() =>
        {
            LeftStickX  = lx;
            LeftStickY  = ly;
            RightStickX = rx;
            RightStickY = ry;
            LeftTrigger  = lt;
            RightTrigger = rt;

            ButtonA     = (btns & GamepadButton.A)             != 0;
            ButtonB     = (btns & GamepadButton.B)             != 0;
            ButtonX     = (btns & GamepadButton.X)             != 0;
            ButtonY     = (btns & GamepadButton.Y)             != 0;
            ButtonLB    = (btns & GamepadButton.LeftShoulder)  != 0;
            ButtonRB    = (btns & GamepadButton.RightShoulder) != 0;
            ButtonStart = (btns & GamepadButton.Start)         != 0;
            ButtonBack  = (btns & GamepadButton.Back)          != 0;
            DpadUp      = (btns & GamepadButton.DPadUp)        != 0;
            DpadDown    = (btns & GamepadButton.DPadDown)      != 0;
            DpadLeft    = (btns & GamepadButton.DPadLeft)      != 0;
            DpadRight   = (btns & GamepadButton.DPadRight)     != 0;
        }, DispatcherPriority.Background);
    }

    private void OnProcessedInput(GamepadState state)
    {
        var now = Environment.TickCount64;
        if (now - _lastProcessedUiTick < UiFrameMs) return;
        _lastProcessedUiTick = now;

        var btns = state.Buttons;

        Dispatcher.UIThread.Post(() =>
        {
            OutDpadUp    = (btns & GamepadButton.DPadUp)   != 0;
            OutDpadDown  = (btns & GamepadButton.DPadDown)  != 0;
            OutDpadLeft  = (btns & GamepadButton.DPadLeft)  != 0;
            OutDpadRight = (btns & GamepadButton.DPadRight) != 0;
            OutButtonA   = (btns & GamepadButton.A)         != 0;
            OutButtonB   = (btns & GamepadButton.B)         != 0;
            OutButtonX   = (btns & GamepadButton.X)         != 0;
            OutButtonY   = (btns & GamepadButton.Y)         != 0;
        }, DispatcherPriority.Background);
    }

    private void OnDeviceChange(InputDevice device, bool connected)
    {
        if (connected)
            ConnectedDevices.Add(device.ToString());
        else
            ConnectedDevices.Remove(device.ToString());
    }
}
