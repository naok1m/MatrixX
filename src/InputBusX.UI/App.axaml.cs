using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using InputBusX.UI.Services;
using InputBusX.UI.Views;

namespace InputBusX.UI;

public class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            ServiceLocator.Initialize();
        }
        catch (Exception ex)
        {
            var crashLog = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "logs", "crash.txt");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
                File.WriteAllText(crashLog,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] STARTUP CRASH\n{ex}\n");
            }
            catch { /* best-effort crash log */ }

            NativeDialog.ShowError(
                "ReflexX — Startup Error",
                $"The application failed to initialize.\n\n{ex.Message}\n\nA crash log was written to:\n{crashLog}");

            Environment.Exit(1);
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var mainWindow = new MainWindow
                {
                    DataContext = ServiceLocator.Resolve<ViewModels.MainViewModel>()
                };
                desktop.MainWindow = mainWindow;

                // Wire the StorageProvider for file open/save dialogs.
                // Must happen after the window is shown so TopLevel is valid.
                mainWindow.Loaded += (_, _) =>
                {
                    var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(mainWindow);
                    if (topLevel is not null)
                        ServiceLocator.Resolve<Services.FileDialogService>().SetTopLevel(topLevel);
                };
            }
            catch (Exception ex)
            {
                NativeDialog.ShowError(
                    "ReflexX — Window Error",
                    $"Failed to create main window.\n\n{ex.Message}");
                Environment.Exit(1);
                return;
            }

            desktop.ShutdownRequested += (_, _) => ServiceLocator.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Shows a native Win32 MessageBox before the Avalonia window is ready.
/// Used only for fatal startup errors where the Avalonia UI cannot be trusted.
/// </summary>
internal static class NativeDialog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    public static void ShowError(string title, string message) =>
        MessageBoxW(IntPtr.Zero, message, title, MB_OK | MB_ICONERROR);
}
