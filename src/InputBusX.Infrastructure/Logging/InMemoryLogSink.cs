using System.Collections.Concurrent;
using InputBusX.Domain.Interfaces;

namespace InputBusX.Infrastructure.Logging;

public sealed class InMemoryLogSink : ILogSink
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 1000;

    public event Action<LogEntry>? LogReceived;

    public IReadOnlyList<LogEntry> RecentEntries => _entries.ToArray();

    public void Write(string level, string category, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message);

        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);

        LogReceived?.Invoke(entry);
    }
}
