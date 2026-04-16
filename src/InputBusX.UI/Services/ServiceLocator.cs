using InputBusX.Application.Filters;
using InputBusX.Application.Interfaces;
using InputBusX.Application.MacroEngine;
using InputBusX.Application.Pipeline;
using InputBusX.Application.Services;
using InputBusX.Domain.Interfaces;
using InputBusX.Infrastructure.Configuration;
using InputBusX.Infrastructure.Input;
using InputBusX.Infrastructure.Logging;
using InputBusX.Infrastructure.Output;
using InputBusX.Infrastructure.Plugins;
using InputBusX.Infrastructure.Data;
using InputBusX.Infrastructure.Vision;
using InputBusX.Infrastructure.HidHide;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InputBusX.UI.Services;

public static class ServiceLocator
{
    private static IServiceProvider? _provider;
    public static IServiceProvider Provider => _provider
        ?? throw new InvalidOperationException("Services not initialized");

    public static T Resolve<T>() where T : notnull => Provider.GetRequiredService<T>();

    public static void Initialize()
    {
        var logSink = new InMemoryLogSink();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("logs/inputbusx-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new SerilogSinkAdapter(logSink))
            .CreateLogger();

        var configPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "settings.json");

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Notification service (UI toast)
        services.AddSingleton<INotificationService, NotificationService>();

        // File dialogs (TopLevel set in App.axaml.cs after window loads)
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());

        // Update check
        services.AddSingleton<IUpdateService, GitHubUpdateService>();

        // Singletons
        services.AddSingleton<InMemoryLogSink>(logSink);
        services.AddSingleton<ILogSink>(sp => sp.GetRequiredService<InMemoryLogSink>());

        services.AddSingleton<IConfigurationStore>(sp =>
            new JsonConfigurationStore(configPath, sp.GetRequiredService<ILogger<JsonConfigurationStore>>()));

        services.AddSingleton<IHidHideService, HidHideService>();
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
        services.AddSingleton<IWeaponDetectionService, OcrWeaponDetectionService>();
        services.AddSingleton<IWeaponLibraryService, WeaponLibraryService>();
        // ViewModels
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.MacroEditorViewModel>();
        services.AddSingleton<ViewModels.ProfilesViewModel>();
        services.AddSingleton<ViewModels.FiltersViewModel>();
        services.AddSingleton<ViewModels.LogsViewModel>();
        services.AddSingleton<ViewModels.WeaponDetectionViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.WeaponLibraryViewModel>();

        _provider = services.BuildServiceProvider();

        // Start config watching
        var configStore = _provider.GetRequiredService<IConfigurationStore>();
        configStore.StartWatching();

        // Whitelist this exe in HidHide on every startup so the app can always
        // read physical controllers, even if they were left hidden from a
        // previous session (HidHide state persists across reboots in the driver).
        try
        {
            var hidHide = _provider.GetService<IHidHideService>();
            hidHide?.EnsureWhitelisted();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HidHide: startup whitelist check failed (non-critical)");
        }
    }

    public static void Shutdown()
    {
        // Stop weapon detection loop cleanly before disposing the container
        try
        {
            var detection = _provider?.GetService<IWeaponDetectionService>();
            detection?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop weapon detection service during shutdown");
        }

        if (_provider is IDisposable disposable)
            disposable.Dispose();
        Log.CloseAndFlush();
    }
}
