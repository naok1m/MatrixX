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
                if (IsDisposed || _webView.CoreWebView2 is null) return;
                try
                {
                    BeginInvoke(() =>
                    {
                        if (!IsDisposed && _webView.CoreWebView2 is not null)
                        {
                            _webView.CoreWebView2.PostWebMessageAsJson(json);
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    // The window handle can disappear while services are shutting down.
                }
            };

            _webView.CoreWebView2.NavigateToString(LoadShellHtml());
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

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            await _bridge.HandleAsync(e.WebMessageAsJson);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebShell command failed: {Message}", e.WebMessageAsJson);
            if (_webView.CoreWebView2 is not null)
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "toast",
                    payload = new { level = "error", message = ex.Message }
                });
                _webView.CoreWebView2.PostWebMessageAsJson(payload);
            }
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
