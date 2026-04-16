using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IOutputController : IDisposable
{
    bool IsConnected { get; }
    void Connect();
    void Disconnect();
    void Update(GamepadState state);
}
