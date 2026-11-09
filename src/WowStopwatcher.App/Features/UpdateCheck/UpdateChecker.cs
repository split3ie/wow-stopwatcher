using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Asks the GitHub releases API whether a newer version than the running one is published.</summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/split3ie/wow-stopwatcher/releases/latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Http = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            using var response = await Http.GetAsync(LatestReleaseUrl, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateStatus.NoReleases);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateStatus.Failed);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var tag = json.RootElement.GetProperty("tag_name").GetString();
            var url = json.RootElement.GetProperty("html_url").GetString();

            var latestText = (tag ?? string.Empty).Trim().TrimStart('v', 'V');
            if (!TryParseVersion(latestText, out var latest) || !TryParseVersion(currentVersion, out var current))
            {
                return new UpdateCheckResult(UpdateStatus.Failed);
            }

            return latest > current
                ? new UpdateCheckResult(UpdateStatus.Available, latestText, url)
                : new UpdateCheckResult(UpdateStatus.UpToDate);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new UpdateCheckResult(UpdateStatus.Failed);
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
