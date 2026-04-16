namespace InputBusX.UI.Services;

public interface IUpdateService
{
    Task<(bool Available, string LatestVersion)> CheckAsync();
}
