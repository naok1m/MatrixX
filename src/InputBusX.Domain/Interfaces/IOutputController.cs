using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IOutputController : IDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// XInput slot (0-3) assigned to the virtual controller after Connect().
    /// Null when not connected. Used to exclude this slot from physical input polling.
    /// </summary>
    int? VirtualXInputSlot { get; }

    void Connect();
    void Disconnect();
    void Update(GamepadState state);
}
