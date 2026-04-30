using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.Infrastructure.Input;

/// <summary>
/// Aggregates XInput (Xbox, Xbox-compatible) and DirectInput (PS4/PS5 DualShock/DualSense,
/// generic HID gamepads) into a single <see cref="IInputProvider"/>. DirectInput filters out
/// controllers that also expose themselves via XInput, so no device is double-reported.
/// </summary>
public sealed class CompositeInputProvider : IInputProvider
{
    private readonly XInputProvider _xinput;
    private readonly DirectInputProvider _dinput;
    private readonly ILogger<CompositeInputProvider> _logger;
    private readonly InputDeviceDeduplicator _deduplicator = new();
    private readonly object _dedupeLock = new();

    public event Action<InputDevice>? DeviceConnected;
    public event Action<InputDevice>? DeviceDisconnected;
    public event Action<string, GamepadState>? StateUpdated;

    public CompositeInputProvider(
        XInputProvider xinput,
        DirectInputProvider dinput,
        ILogger<CompositeInputProvider> logger)
    {
        _xinput = xinput;
        _dinput = dinput;
        _logger = logger;

        _xinput.DeviceConnected += d =>
        {
            lock (_dedupeLock) { _deduplicator.Connect(d); }
            DeviceConnected?.Invoke(d);
        };
        _xinput.DeviceDisconnected += d =>
        {
            lock (_dedupeLock) { _deduplicator.Disconnect(d); }
            DeviceDisconnected?.Invoke(d);
        };
        _xinput.StateUpdated       += (id, s) => StateUpdated?.Invoke(id, s);

        _dinput.DeviceConnected += d =>
        {
            var visible = true;
            lock (_dedupeLock) { visible = _deduplicator.Connect(d); }
            if (visible)
                DeviceConnected?.Invoke(d);
            else
                _logger.LogInformation("DirectInput: suppressing duplicate stream for [{Name}]", d.Name);
        };
        _dinput.DeviceDisconnected += d =>
        {
            var visible = true;
            lock (_dedupeLock)
            {
                visible = _deduplicator.ShouldPublish(d.Id);
                _deduplicator.Disconnect(d);
            }
            if (visible)
                DeviceDisconnected?.Invoke(d);
        };
        _dinput.StateUpdated += (id, s) =>
        {
            lock (_dedupeLock)
            {
                if (!_deduplicator.ShouldPublish(id))
                    return;
            }

            StateUpdated?.Invoke(id, s);
        };
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _xinput.StartAsync(ct);
        await _dinput.StartAsync(ct);
        _logger.LogInformation("Input provider started (XInput + DirectInput)");
    }

    public async Task StopAsync()
    {
        await _xinput.StopAsync();
        await _dinput.StopAsync();
    }

    public IReadOnlyList<InputDevice> GetConnectedDevices()
    {
        var list = new List<InputDevice>();
        list.AddRange(_xinput.GetConnectedDevices());
        list.AddRange(_dinput.GetConnectedDevices());
        lock (_dedupeLock)
            return _deduplicator.FilterConnected(list);
    }

    public void Dispose()
    {
        // The DI container owns the concrete providers and disposes them separately.
        // Disposing them here as well causes double-dispose shutdown races.
    }
}
