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

        try
        {
            Thread.Sleep(100); // brief delay to ensure file write is complete
            var config = Load();
            ConfigurationChanged?.Invoke(config);
            _logger.LogInformation("Configuration hot-reloaded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hot-reload configuration");
        }
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
