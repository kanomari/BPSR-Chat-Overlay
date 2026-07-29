using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace BPSRChatOverlay.Updates;

public static class UpdateCheckService
{
    public static readonly Uri ReleasesPageUri =
        new("https://github.com/kanomari/BPSR-Chat-Overlay/releases");

    private static readonly Uri LatestReleaseApiUri =
        new("https://api.github.com/repos/kanomari/BPSR-Chat-Overlay/releases/latest");

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUri);
            using HttpResponseMessage response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Information(
                    "No published stable GitHub Release is currently available");
                return new UpdateCheckResult(
                    UpdateCheckStatus.NoStableRelease,
                    AppVersionProvider.CurrentVersion);
            }

            response.EnsureSuccessStatusCode();

            await using Stream contentStream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubReleaseResponse? release =
                await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(
                    contentStream,
                    cancellationToken: cancellationToken);

            if (release is null ||
                !AppVersionProvider.TryParseVersionTag(
                    release.TagName,
                    out Version latestVersion))
            {
                Log.Warning(
                    "GitHub release response contained an invalid tag. TagName: {TagName}",
                    release?.TagName);
                return Failed();
            }

            Uri releasePageUri = Uri.TryCreate(
                    release.HtmlUrl,
                    UriKind.Absolute,
                    out Uri? parsedReleasePageUri)
                ? parsedReleasePageUri
                : ReleasesPageUri;

            return new UpdateCheckResult(
                UpdateCheckStatus.Success,
                AppVersionProvider.CurrentVersion,
                latestVersion,
                releasePageUri);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug("Update check was cancelled");
            return new UpdateCheckResult(
                UpdateCheckStatus.Cancelled,
                AppVersionProvider.CurrentVersion);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to check GitHub Releases for updates. ApiUrl: {ApiUrl}",
                LatestReleaseApiUri);
            return Failed();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "BPSR-Chat-Overlay",
                AppVersionProvider.CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
        client.DefaultRequestHeaders.Add(
            "X-GitHub-Api-Version",
            "2022-11-28");
        return client;
    }

    private static UpdateCheckResult Failed()
    {
        return new UpdateCheckResult(
            UpdateCheckStatus.Failed,
            AppVersionProvider.CurrentVersion);
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
