using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WindowsCleaner.Core;

public abstract record UpdateResult
{
    private UpdateResult() { }
    public sealed record UpToDate : UpdateResult;
    public sealed record UpdateAvailable(Version Latest, string ReleaseUrl) : UpdateResult;
    public sealed record NoReleases : UpdateResult;
    public sealed record Failed(string Message) : UpdateResult;
}

/// <summary>Queries the GitHub releases API on demand. Never downloads or opens anything.</summary>
public sealed class UpdateChecker(HttpClient http)
{
    public const string LatestReleaseApi = "https://api.github.com/repos/BobCFF/WindowsCleaner/releases/latest";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<UpdateResult> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindowsCleaner", current.ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateResult.NoReleases();
            if (!response.IsSuccessStatusCode) return new UpdateResult.Failed($"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return Evaluate(body, current);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateResult.Failed("Timed out");
        }
        catch (OperationCanceledException) { return new UpdateResult.Failed("Cancelled"); }
        catch (Exception ex) { return new UpdateResult.Failed(ex.Message); }
    }

    private static UpdateResult Evaluate(string body, Version current)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return new UpdateResult.Failed("Invalid response"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new UpdateResult.Failed("Invalid response");

            if (Flag(root, "prerelease") || Flag(root, "draft")) return new UpdateResult.UpToDate();

            if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.ValueKind != JsonValueKind.String
                || !TryParseTag(tagEl.GetString(), out var latest))
                return new UpdateResult.Failed("Unrecognised release version");

            if (!root.TryGetProperty("html_url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String
                || !IsTrustedReleaseUrl(urlEl.GetString(), out var url))
                return new UpdateResult.Failed("Unexpected release URL");

            return Normalize(latest) > Normalize(current) ? new UpdateResult.UpdateAvailable(latest, url) : new UpdateResult.UpToDate();
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.True;

    // Version("1.0") < Version("1.0.0") because unset parts are -1; compare on a 4-part basis.
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    internal static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var t = tag.Trim();
        if (t[0] is 'v' or 'V') t = t[1..];
        var cut = t.IndexOfAny(['-', '+']);
        if (cut >= 0) t = t[..cut];
        var parts = t.Split('.');
        if (parts.Length is < 2 or > 4) return false;
        if (!parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit) && int.TryParse(p, out _))) return false;
        version = Version.Parse(t);
        return true;
    }

    public static bool IsTrustedReleaseUrl(string? s, out string url)
    {
        url = "";
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps || u.Host != "github.com" || !u.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        if (!u.AbsolutePath.StartsWith("/BobCFF/WindowsCleaner/", StringComparison.Ordinal)) return false;
        if (u.AbsolutePath.Split('/').Contains("..")) return false;
        url = u.AbsoluteUri;
        return true;
    }
}
