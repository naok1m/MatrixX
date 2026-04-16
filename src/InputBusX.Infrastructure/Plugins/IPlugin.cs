using InputBusX.Domain.Entities;

namespace InputBusX.Infrastructure.Plugins;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }

    void Initialize();
    GamepadState Process(GamepadState state);
    void Shutdown();
}
