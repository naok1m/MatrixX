using Serilog.Core;
using Serilog.Events;

namespace InputBusX.Infrastructure.Logging;

public sealed class SerilogSinkAdapter : ILogEventSink
{
    private readonly InMemoryLogSink _sink;

    public SerilogSinkAdapter(InMemoryLogSink sink)
    {
        _sink = sink;
    }

    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "TRACE",
            LogEventLevel.Debug => "DEBUG",
            LogEventLevel.Information => "INFO",
            LogEventLevel.Warning => "WARN",
            LogEventLevel.Error => "ERROR",
            LogEventLevel.Fatal => "FATAL",
            _ => "INFO"
        };

        var category = logEvent.Properties.TryGetValue("SourceContext", out var ctx)
            ? ctx.ToString().Trim('"').Split('.').LastOrDefault() ?? "System"
            : "System";

        _sink.Write(level, category, logEvent.RenderMessage());
    }
}
