namespace InputBusX.Domain.Interfaces;

public interface ILogSink
{
    event Action<LogEntry>? LogReceived;
    IReadOnlyList<LogEntry> RecentEntries { get; }
}

public record LogEntry(
    DateTime Timestamp,
    string Level,
    string Category,
    string Message
);
