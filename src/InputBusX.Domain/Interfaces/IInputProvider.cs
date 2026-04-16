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

    /// <summary>
    /// Exclude specific XInput slot indices from polling.
    /// Used to prevent reading back the ViGEm virtual controller as physical input.
    /// Default: no-op (providers that don't use XInput can ignore this).
    /// </summary>
    void ExcludeXInputSlots(int[] slots) { }
}
