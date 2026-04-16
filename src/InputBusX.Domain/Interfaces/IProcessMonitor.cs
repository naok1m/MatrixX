namespace InputBusX.Domain.Interfaces;

public interface IProcessMonitor : IDisposable
{
    string? GetForegroundProcessName();
    Task StartAsync(CancellationToken ct);
}
