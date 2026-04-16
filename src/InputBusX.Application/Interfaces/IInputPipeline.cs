using InputBusX.Domain.Entities;

namespace InputBusX.Application.Interfaces;

public interface IInputPipeline : IDisposable
{
    event Action<GamepadState>? InputProcessed;
    event Action<GamepadState>? RawInputReceived;

    bool IsRunning { get; }
    bool ViGEmAvailable { get; }
    int? VirtualXInputSlot { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
