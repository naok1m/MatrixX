using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IInputProvider : IDisposable
{
    event Action<InputDevice>? DeviceConnected;
    event Action<InputDevice>? DeviceDisconnected;
    event Action<string, GamepadState>? StateUpdated;

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    IReadOnlyList<InputDevice> GetConnectedDevices();
}
