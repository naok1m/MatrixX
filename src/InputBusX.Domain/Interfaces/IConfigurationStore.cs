using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IConfigurationStore
{
    event Action<AppConfiguration>? ConfigurationChanged;

    AppConfiguration Load();
    void Save(AppConfiguration config);
    void StartWatching();
    void StopWatching();
}
