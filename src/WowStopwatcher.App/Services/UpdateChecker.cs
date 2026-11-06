using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WowStopwatcher.App.Services;

/// <summary>Asks the GitHub releases API whether a newer version than the running one is published.</summary>
public sealed class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/split3ie/wow-stopwatcher/releases/latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Http = CreateClient();

    public enum UpdateStatus
    {
        UpToDate,
        Available,
        NoReleases,
        Failed,
    }

    /// <summary>Outcome of a check; <see cref="Version"/> and <see cref="Url"/> are set only when Available.</summary>
    public sealed record Result(UpdateStatus Status, string? Version = null, string? Url = null);

    /// <summary>Compares the latest release tag with <paramref name="currentVersion"/> (for example "0.1.0").</summary>
    public async Task<Result> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            using var response = await Http.GetAsync(LatestReleaseUrl, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Result(UpdateStatus.NoReleases);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new Result(UpdateStatus.Failed);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var tag = json.RootElement.GetProperty("tag_name").GetString();
            var url = json.RootElement.GetProperty("html_url").GetString();

            var latestText = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
            if (!TryParseVersion(latestText, out var latest) || !TryParseVersion(currentVersion, out var current))
            {
                return new Result(UpdateStatus.Failed);
            }

            return latest > current
                ? new Result(UpdateStatus.Available, latestText, url)
                : new Result(UpdateStatus.UpToDate);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new Result(UpdateStatus.Failed);
        }
    }

    // "1.2.3-beta.1+abc" -> 1.2.3 (pre-release and build suffixes are ignored).
    private static bool TryParseVersion(string text, out Version version)
    {
        var core = text.Split('+', '-')[0];
        return Version.TryParse(core, out version!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WowStopwatcher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
