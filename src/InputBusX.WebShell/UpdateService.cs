using Serilog;
using Velopack;
using Velopack.Sources;

namespace InputBusX.WebShell;

/// <summary>
/// Lightweight wrapper around <see cref="UpdateManager"/> that pulls release
/// metadata from the project's GitHub Releases. Runs entirely off the UI
/// thread so a slow/blocked network never affects input latency.
/// </summary>
public sealed class UpdateService
{
    private const string GitHubRepoUrl = "https://github.com/naok1m/MatrixX";

    private readonly UpdateManager? _manager;
    private VelopackAsset? _pendingUpdate;

    public UpdateService()
    {
        try
        {
            // Pre-release: false. Switch to true once a beta channel exists.
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            _manager = new UpdateManager(source);
            _pendingUpdate = _manager.UpdatePendingRestart;
        }
        catch (Exception ex)
        {
            // App was launched outside an installed Velopack package
            // (e.g. running from `dotnet run` or the raw publish folder).
            // No update support is possible — the manager stays null and
            // CheckForUpdatesAsync will simply return null.
            Log.Debug(ex, "Velopack UpdateManager could not be initialized — running in non-installed mode");
            _manager = null;
        }
    }

    public bool IsInstalled => _manager?.IsInstalled ?? false;
    public string? PendingVersion => _pendingUpdate?.Version?.ToString();

    /// <summary>
    /// Checks GitHub for a newer release. Returns the version string if
    /// one is available and downloaded, or null otherwise.
    /// </summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (_manager is null || !_manager.IsInstalled) return null;

        try
        {
            _pendingUpdate ??= _manager.UpdatePendingRestart;
            if (_pendingUpdate is not null)
            {
                var pendingVersion = _pendingUpdate.Version?.ToString() ?? "?";
                Log.Information("Update {Version} is already staged and pending restart", pendingVersion);
                return pendingVersion;
            }

            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) return null;

            await _manager.DownloadUpdatesAsync(info, cancelToken: ct).ConfigureAwait(false);
            _pendingUpdate = _manager.UpdatePendingRestart ?? info.TargetFullRelease;

            var v = _pendingUpdate?.Version?.ToString() ?? info.TargetFullRelease?.Version?.ToString() ?? "?";
            Log.Information("Update {Version} downloaded and ready to apply", v);
            return v;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            return null;
        }
    }

    /// <summary>
    /// Restarts the app onto the downloaded update. Returns false if no
    /// update is staged. Does not return when an update is applied — the
    /// process is replaced by the updater.
    /// </summary>
    public bool ApplyAndRestart()
    {
        if (_manager is null) return false;
        try
        {
            _pendingUpdate ??= _manager.UpdatePendingRestart;
            if (_pendingUpdate is null) return false;

            _manager.ApplyUpdatesAndRestart(_pendingUpdate);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply update");
            return false;
        }
    }
}
