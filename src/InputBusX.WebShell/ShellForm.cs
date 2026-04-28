using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;
using System.Reflection;
using System.Runtime.InteropServices;

namespace InputBusX.WebShell;

public sealed class ShellForm : Form
{
    private readonly WebShellServices _services;
    private readonly WebShellBridge _bridge;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly UpdateService _updateService = new();

    public ShellForm(WebShellServices services)
    {
        _services = services;
        _bridge = new WebShellBridge(services);

        Text = "ReflexX";
        BackColor = Color.FromArgb(6, 10, 16);
        MinimumSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1320, 820);
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);

        Controls.Add(_webView);
        Load += OnLoad;
        FormClosing += (_, _) => _services.Shutdown();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.UseDarkCaption(Handle);
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReflexX",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            _webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder
            };

            await _webView.EnsureCoreWebView2Async();

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _bridge.StateChanged += (_, json) =>
            {
                // Do NOT touch _webView.CoreWebView2 here — this handler fires on
                // background threads (pipeline, log sink, device events) and the
                // CoreWebView2 getter throws "can only be accessed from the UI
                // thread" on newer WebView2 SDKs. Marshal first, check after.
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(() =>
                    {
                        if (IsDisposed || _webView.IsDisposed) return;
                        var core = _webView.CoreWebView2;
                        if (core is null) return;
                        try { core.PostWebMessageAsJson(json); }
                        catch (ObjectDisposedException) { /* shutting down */ }
                        catch (InvalidOperationException) { /* shutting down */ }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // Stop button can race with form disposal.
                }
                catch (InvalidOperationException)
                {
                    // The window handle can disappear while services are shutting down.
                }
            };

            _webView.CoreWebView2.NavigateToString(LoadShellHtml());

            // Background update check. Delayed so the UI loads first and the
            // user isn't blocked. Runs only once per launch — Velopack does
            // its own caching and noop'ing when no update is available.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                var version = await _updateService.CheckAndDownloadAsync().ConfigureAwait(false);
                if (version is null) return;
                NotifyUpdateAvailable(version);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize the ReflexX WebShell");
            MessageBox.Show(this,
                $"Failed to initialize ReflexX UI:\n\n{ex.Message}",
                "ReflexX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private void NotifyUpdateAvailable(string version)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "updateAvailable",
            payload = new { version }
        });
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || _webView.IsDisposed) return;
                var core = _webView.CoreWebView2;
                if (core is null) return;
                try { core.PostWebMessageAsJson(payload); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;

        // Intercept the update-apply command before delegating to the bridge.
        // ApplyUpdatesAndRestart kills the process, so anything after must
        // not be relied on.
        if (json.Contains("\"applyUpdate\"", StringComparison.Ordinal))
        {
            try
            {
                _services.Shutdown(); // unplug ViGEm before the updater takes over
                if (_updateService.ApplyAndRestart()) return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Apply-update failed");
            }
        }

        try
        {
            await _bridge.HandleAsync(json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebShell command failed: {Message}", json);
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "toast",
                payload = new { level = "error", message = ex.Message }
            });
            // Continuation after await may resume on a thread-pool thread —
            // marshal back to the UI thread before touching CoreWebView2.
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (IsDisposed || _webView.IsDisposed) return;
                    var core = _webView.CoreWebView2;
                    if (core is null) return;
                    try { core.PostWebMessageAsJson(payload); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }

    private static string LoadShellHtml()
    {
        var html = ReadResourceText("wwwroot/index.html");
        var css = ReadResourceText("wwwroot/styles.css");
        var js = ReadResourceText("wwwroot/app.js");
        var logo = ReadResourceBase64("wwwroot/logo-small.png");

        return html
            .Replace("<link rel=\"stylesheet\" href=\"./styles.css\" />", $"<style>{css}</style>")
            .Replace("<img src=\"./logo.png\" alt=\"ReflexX\" />", $"<img src=\"data:image/png;base64,{logo}\" alt=\"ReflexX\" />")
            .Replace("<script src=\"./app.js\"></script>", $"<script>{js}</script>");
    }

    private static string ReadResourceText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReadResourceBase64(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Convert.ToBase64String(memory.ToArray());
    }

    private static class WindowChrome
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        public static void UseDarkCaption(IntPtr handle)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return;
            }

            var enabled = 1;
            _ = DwmSetWindowAttribute(
                handle,
                DwmwaUseImmersiveDarkMode,
                ref enabled,
                Marshal.SizeOf<int>());
        }
    }
}
