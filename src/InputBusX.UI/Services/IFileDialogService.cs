namespace InputBusX.UI.Services;

public interface IFileDialogService
{
    Task<string?> OpenFileAsync(string title, params (string Name, string[] Patterns)[] filters);
    Task<string?> SaveFileAsync(string title, string suggestedName, params (string Name, string[] Patterns)[] filters);
}
