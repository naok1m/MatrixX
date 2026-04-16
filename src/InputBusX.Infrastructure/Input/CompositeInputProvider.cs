using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.Infrastructure.Input;

/// <summary>
/// Wraps XInputProvider only. DirectInput is kept available but disabled by default
/// to avoid double-picking controllers that already work via XInput.
/// </summary>
public sealed class CompositeInputProvider : IInputProvider
{
    private readonly XInputProvider _xinput;
    private readonly ILogger<CompositeInputProvider> _logger;

    public event Action<InputDevice>? DeviceConnected;
    public event Action<InputDevice>? DeviceDisconnected;
    public event Action<string, GamepadState>? StateUpdated;

    public CompositeInputProvider(
        XInputProvider xinput,
        DirectInputProvider dinput,          // kept in DI graph, just not used
        ILogger<CompositeInputProvider> logger)
    {
        _xinput = xinput;
        _logger = logger;

        _xinput.DeviceConnected    += d => DeviceConnected?.Invoke(d);
        _xinput.DeviceDisconnected += d => DeviceDisconnected?.Invoke(d);
        _xinput.StateUpdated       += (id, s) => StateUpdated?.Invoke(id, s);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _xinput.StartAsync(ct);
        _logger.LogInformation("Input provider started (XInput only)");
    }

    public async Task StopAsync()
    {
        await _xinput.StopAsync();
    }

    public IReadOnlyList<InputDevice> GetConnectedDevices()
        => _xinput.GetConnectedDevices();

    public void Dispose()
    {
        _xinput.Dispose();
    }
}
