using System.Reflection;
using System.Text.Json;

namespace InputBusX.UI.Services;

/// <summary>
/// Checks the GitHub releases API for a newer version of MatrixX.
/// Non-blocking — all exceptions are swallowed so a network failure
/// never breaks startup.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    // ── Change these when the repo goes public ──────────────────────────
    private const string ReleasesUrl =
        "https://api.github.com/repos/naoki-dev/matrixx/releases/latest";

    public async Task<(bool Available, string LatestVersion)> CheckAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Add("User-Agent", "MatrixX-UpdateCheck/1.0");

            var json = await client.GetStringAsync(ReleasesUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return (false, "");
            var latestStr = (tagProp.GetString() ?? "").TrimStart('v');

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (Version.TryParse(latestStr, out var latest) && latest > current)
                return (true, latestStr);
        }
        catch
        {
            // Update check is non-critical — network errors, 404, JSON parse
            // failures must never surface to the user.
        }

        return (false, "");
    }
}
