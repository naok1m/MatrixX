using InputBusX.Application.Filters;
using InputBusX.Application.Interfaces;
using InputBusX.Application.MacroEngine;
using InputBusX.Application.Pipeline;
using InputBusX.Application.Services;
using InputBusX.Domain.Interfaces;
using InputBusX.Infrastructure.Configuration;
using InputBusX.Infrastructure.Data;
using InputBusX.Infrastructure.Input;
using InputBusX.Infrastructure.Logging;
using InputBusX.Infrastructure.Output;
using InputBusX.Infrastructure.Plugins;
using InputBusX.Infrastructure.Vision;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InputBusX.WebShell;

public sealed class WebShellServices : IDisposable
{
    private ServiceProvider? _provider;

    public IServiceProvider Provider => _provider
        ?? throw new InvalidOperationException("Services have not been initialized.");

    public T Resolve<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Initialize()
    {
        var logSink = new InMemoryLogSink();
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReflexX");
        var logsPath = Path.Combine(appDataPath, "logs");
        var profilesPath = Path.Combine(appDataPath, "profiles");
        Directory.CreateDirectory(logsPath);
        Directory.CreateDirectory(profilesPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logsPath, "inputbusx-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new SerilogSinkAdapter(logSink))
            .CreateLogger();

        var configPath = Path.Combine(AppContext.BaseDirectory, "config", "settings.json");

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddSingleton<InMemoryLogSink>(logSink);
        services.AddSingleton<ILogSink>(sp => sp.GetRequiredService<InMemoryLogSink>());
        services.AddSingleton<IConfigurationStore>(sp =>
            new JsonConfigurationStore(configPath, sp.GetRequiredService<ILogger<JsonConfigurationStore>>()));
        services.AddSingleton<IProcessMonitor, ProcessMonitor>();
        services.AddSingleton<IProfileManager, ProfileManagerService>();
        services.AddSingleton<XInputProvider>();
        services.AddSingleton<DirectInputProvider>();
        services.AddSingleton<IInputProvider, CompositeInputProvider>();
        services.AddSingleton<IOutputController, ViGEmOutputController>();
        services.AddSingleton<IMacroProcessor, MacroProcessor>();
        services.AddSingleton<IInputFilter, CompositeInputFilter>();
        services.AddSingleton<IInputPipeline, InputPipeline>();
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<IWeaponDetectionService, TemplateWeaponDetectionService>();
        services.AddSingleton<IWeaponLibraryService, WeaponLibraryService>();

        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<IConfigurationStore>().StartWatching();
    }

    public void Shutdown()
    {
        try
        {
            Resolve<IWeaponDetectionService>().StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop weapon detection service during shutdown");
        }

        _provider?.Dispose();
        Log.CloseAndFlush();
    }

    public void Dispose() => Shutdown();
}
