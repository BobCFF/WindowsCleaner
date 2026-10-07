using System.Net;
using System.Net.Http;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    private static string Release(string tag, string url = "https://github.com/BobCFF/WindowsCleaner/releases/tag/v1.2.0",
        bool prerelease = false, bool draft = false) =>
        $"{{\"tag_name\":\"{tag}\",\"html_url\":\"{url}\",\"prerelease\":{prerelease.ToString().ToLower()},\"draft\":{draft.ToString().ToLower()}}}";

    private static Task<UpdateResult> Check(FakeHandler h, string current = "1.0.0") =>
        new UpdateChecker(new HttpClient(h)).CheckAsync(Version.Parse(current), CancellationToken.None);

    [Fact]
    public async Task Same_version_is_UpToDate()
    {
        Assert.IsType<UpdateResult.UpToDate>(await Check(new FakeHandler(_ => Json(Release("v1.0.0")))));
    }

    [Fact]
    public async Task Newer_version_is_UpdateAvailable_with_url()
    {
        var r = await Check(new FakeHandler(_ => Json(Release("v1.2.0"))));
        var u = Assert.IsType<UpdateResult.UpdateAvailable>(r);
        Assert.Equal(new Version(1, 2, 0), u.Latest);
        Assert.Equal("https://github.com/BobCFF/WindowsCleaner/releases/tag/v1.2.0", u.ReleaseUrl);
    }

    [Fact]
    public async Task Older_tag_is_UpToDate()
    {
        Assert.IsType<UpdateResult.UpToDate>(await Check(new FakeHandler(_ => Json(Release("v0.9.0")))));
    }

    [Theory]
    [InlineData("v2.0", "2.0")]
    [InlineData("V2.1.3", "2.1.3")]
    [InlineData("2.1.3.4", "2.1.3.4")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    public async Task Tag_formats_are_parsed(string tag, string expected)
    {
        var r = await Check(new FakeHandler(_ => Json(Release(tag))));
        Assert.Equal(Version.Parse(expected), Assert.IsType<UpdateResult.UpdateAvailable>(r).Latest);
    }

    [Fact]
    public async Task Http_404_is_NoReleases()
    {
        Assert.IsType<UpdateResult.NoReleases>(await Check(new FakeHandler(_ => Json("{}", HttpStatusCode.NotFound))));
    }

    [Fact]
    public async Task Http_500_is_Failed()
    {
        Assert.IsType<UpdateResult.Failed>(await Check(new FakeHandler(_ => Json("{}", HttpStatusCode.InternalServerError))));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"html_url\":\"https://github.com/BobCFF/WindowsCleaner/releases/tag/v2\"}")]
    [InlineData("{\"tag_name\":\"banana\",\"html_url\":\"https://github.com/BobCFF/WindowsCleaner/releases/tag/v2\"}")]
    [InlineData("{\"tag_name\":\"v2.0.0\"}")]
    public async Task Bad_payloads_are_Failed(string body)
    {
        Assert.IsType<UpdateResult.Failed>(await Check(new FakeHandler(_ => Json(body))));
    }

    [Theory]
    [InlineData("https://evil.example/BobCFF/WindowsCleaner/releases/tag/v2")]
    [InlineData("http://github.com/BobCFF/WindowsCleaner/releases/tag/v2")]
    [InlineData("https://github.com/Other/Repo/releases/tag/v2")]
    [InlineData("https://github.com.evil.example/BobCFF/WindowsCleaner/releases")]
    [InlineData("https://github.com/BobCFF/WindowsCleanerEvil/releases")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/../Other/Repo")]
    [InlineData("not a url")]
    public async Task Foreign_release_url_is_Failed(string url)
    {
        var r = await Check(new FakeHandler(_ => Json(Release("v2.0.0", url))));
        var f = Assert.IsType<UpdateResult.Failed>(r);
        Assert.Equal("Unexpected release URL", f.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Prerelease_or_draft_is_not_latest(bool pre, bool draft)
    {
        Assert.IsType<UpdateResult.UpToDate>(await Check(new FakeHandler(_ => Json(Release("v9.0.0", prerelease: pre, draft: draft)))));
    }

    [Fact]
    public async Task Network_exception_is_Failed()
    {
        var r = await Check(new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Contains("offline", Assert.IsType<UpdateResult.Failed>(r).Message);
    }

    [Fact]
    public async Task Request_uses_exact_url_and_headers()
    {
        var h = new FakeHandler(_ => Json(Release("v1.0.0")));
        await Check(h);
        Assert.Equal(HttpMethod.Get, h.Request!.Method);
        Assert.Equal("https://api.github.com/repos/BobCFF/WindowsCleaner/releases/latest", h.Request.RequestUri!.AbsoluteUri);
        Assert.Contains("WindowsCleaner", h.Request.Headers.UserAgent.ToString());
        Assert.Contains(h.Request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }
}
