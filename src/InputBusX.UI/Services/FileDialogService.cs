using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace InputBusX.UI.Services;

/// <summary>
/// Wraps Avalonia's StorageProvider for file open/save dialogs.
/// Call <see cref="SetTopLevel"/> once after the main window is shown.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    private TopLevel? _topLevel;

    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<string?> OpenFileAsync(string title,
        params (string Name, string[] Patterns)[] filters)
    {
        if (_topLevel?.StorageProvider is not { } sp) return null;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = title,
            AllowMultiple  = false,
            FileTypeFilter = ToFileTypes(filters)
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName,
        params (string Name, string[] Patterns)[] filters)
    {
        if (_topLevel?.StorageProvider is not { } sp) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices   = ToFileTypes(filters)
        });

        return file?.TryGetLocalPath();
    }

    private static IReadOnlyList<FilePickerFileType> ToFileTypes(
        (string Name, string[] Patterns)[] filters) =>
        filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns })
               .ToList();
}
