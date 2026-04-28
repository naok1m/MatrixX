using System.Text.Json;
using System.Text.Json.Serialization;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.Infrastructure.Configuration;

public sealed class JsonConfigurationStore : IConfigurationStore, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<JsonConfigurationStore> _logger;
    private FileSystemWatcher? _watcher;
    private DateTime _lastWriteTime;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public event Action<AppConfiguration>? ConfigurationChanged;

    public JsonConfigurationStore(string filePath, ILogger<JsonConfigurationStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public AppConfiguration Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    var defaultConfig = CreateDefaultConfig();
                    Save(defaultConfig);
                    return defaultConfig;
                }

                var json = File.ReadAllText(_filePath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
                _logger.LogInformation("Configuration loaded from {Path}", _filePath);
                return config ?? CreateDefaultConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration, using defaults");
                return CreateDefaultConfig();
            }
        }
    }

    public void Save(AppConfiguration config)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(config, JsonOptions);

                // Atomic write: write to .tmp then rename so a crash mid-write
                // never corrupts the live config file.
                var tmpPath = _filePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                _lastWriteTime = DateTime.UtcNow;
                File.Move(tmpPath, _filePath, overwrite: true);

                _logger.LogDebug("Configuration saved to {Path}", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {Path}", _filePath);
                throw; // re-throw so callers (ViewModels) can show the error to the user
            }
        }
    }

    public void StartWatching()
    {
        var dir = Path.GetDirectoryName(_filePath);
        var file = Path.GetFileName(_filePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        Directory.CreateDirectory(dir);

        // Ensure the config file exists before watching
        if (!File.Exists(_filePath))
        {
            var defaultConfig = CreateDefaultConfig();
            Save(defaultConfig);
        }

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _logger.LogInformation("Watching configuration file for changes");
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if ((DateTime.UtcNow - _lastWriteTime).TotalSeconds < 1)
            return;

        // FileSystemWatcher can fire Changed before the external writer has
        // finished flushing — and a fixed Thread.Sleep(100) is unreliable on
        // slow disks or when the writer holds the handle longer. Retry the
        // load with backoff while the file is still locked / partially
        // written, then surface the failure if it really won't open.
        try
        {
            var config = LoadWithRetry();
            ConfigurationChanged?.Invoke(config);
            _logger.LogInformation("Configuration hot-reloaded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hot-reload configuration");
        }
    }

    private AppConfiguration LoadWithRetry()
    {
        const int maxAttempts = 8;
        int delay = 25;
        Exception? last = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                // Open with shared read so we don't trip on the writer's handle.
                using var stream = new FileStream(
                    _filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json))
                    throw new IOException("Configuration file is empty (writer may not have flushed yet)");

                var config = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
                return config ?? CreateDefaultConfig();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                last = ex;
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 500);
            }
        }

        throw last ?? new IOException("Configuration reload failed for an unknown reason");
    }

    private static AppConfiguration CreateDefaultConfig()
    {
        var defaultProfile = new Profile
        {
            Name = "Default",
            IsDefault = true
        };

        return new AppConfiguration
        {
            ActiveProfileId = defaultProfile.Id,
            Profiles = [defaultProfile],
            General = new GeneralSettings()
        };
    }

    public void Dispose()
    {
        StopWatching();
    }
}
