using System.Reflection;
using Microsoft.Extensions.Logging;

namespace InputBusX.Infrastructure.Plugins;

public sealed class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly List<IPlugin> _plugins = [];

    public IReadOnlyList<IPlugin> LoadedPlugins => _plugins.AsReadOnly();

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public void LoadFromDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogDebug("Plugin directory does not exist: {Path}", path);
            return;
        }

        foreach (var dll in Directory.GetFiles(path, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        plugin.Initialize();
                        _plugins.Add(plugin);
                        _logger.LogInformation("Loaded plugin: {Name} v{Version}", plugin.Name, plugin.Version);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin from {DllPath}", dll);
            }
        }
    }

    public void UnloadAll()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.Shutdown(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error shutting down plugin {Name}", plugin.Name);
            }
        }
        _plugins.Clear();
    }
}
