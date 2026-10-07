using System.Net;
using System.Net.Http;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class UpdateCheckerHardeningTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }

    private static FakeHandler Body(string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));

    private static string Release(string tag, string url = "https://github.com/BobCFF/WindowsCleaner/releases/tag/v9") =>
        $"{{\"tag_name\":\"{tag}\",\"html_url\":\"{url}\"}}";

    private static Task<UpdateResult> Check(HttpMessageHandler h, string current = "1.0.0", CancellationToken ct = default) =>
        new UpdateChecker(new HttpClient(h)).CheckAsync(Version.Parse(current), ct);

    [Theory]
    [InlineData("https://github.com@evil.com/BobCFF/WindowsCleaner/x")]
    [InlineData("https://user:pw@github.com/BobCFF/WindowsCleaner/x")]
    [InlineData("https://github.com:444/BobCFF/WindowsCleaner/x")]
    [InlineData("https://github.com./BobCFF/WindowsCleaner/x")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/%2e%2e/Other/Repo")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/%2E%2E/Other/Repo")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/../Other/Repo")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/..%2fOther/Repo")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/%2e%2e%2fOther")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner/..%5cOther")]
    [InlineData("https://github.com/BobCFF/WindowsCleaner-evil/releases")]
    [InlineData(@"https://github.com/BobCFF/WindowsCleaner/..\Other\Repo")]
    [InlineData(@"https://github.com\BobCFF\WindowsCleaner\x")]
    [InlineData("https://github.com/bobcff/windowscleaner/x")]
    public void Validator_rejects(string url) =>
        Assert.False(UpdateChecker.IsTrustedReleaseUrl(url, out _));

    [Fact]
    public void Validator_accepts_uppercase_host_and_returns_normalized_url()
    {
        Assert.True(UpdateChecker.IsTrustedReleaseUrl("https://GitHub.COM/BobCFF/WindowsCleaner/releases/tag/v2", out var url));
        Assert.Equal("https://github.com/BobCFF/WindowsCleaner/releases/tag/v2", url);
    }

    [Fact]
    public void Validator_accepts_query_string()
    {
        Assert.True(UpdateChecker.IsTrustedReleaseUrl("https://github.com/BobCFF/WindowsCleaner/releases/tag/v2?x=1", out var url));
        Assert.EndsWith("?x=1", url);
    }

    [Theory]
    [InlineData("v99999999999.0")]
    [InlineData("v1")]
    [InlineData("v1.2.3.4.5")]
    public async Task Bad_tags_are_Failed(string tag) =>
        Assert.IsType<UpdateResult.Failed>(await Check(Body(Release(tag))));

    [Fact]
    public async Task Two_part_tag_equal_to_three_part_current_is_UpToDate() =>
        Assert.IsType<UpdateResult.UpToDate>(await Check(Body(Release("v1.0")), "1.0.0"));

    [Fact]
    public async Task Two_part_current_works_and_equal_is_UpToDate() =>
        Assert.IsType<UpdateResult.UpToDate>(await Check(Body(Release("v1.0.0")), "1.0"));

    [Fact]
    public async Task Two_part_current_older_than_tag_is_UpdateAvailable() =>
        Assert.IsType<UpdateResult.UpdateAvailable>(await Check(Body(Release("v1.1.0")), "1.0"));

    [Fact]
    public async Task Four_part_comparison()
    {
        Assert.IsType<UpdateResult.UpdateAvailable>(await Check(Body(Release("v1.0.0.1")), "1.0.0.0"));
        Assert.IsType<UpdateResult.UpToDate>(await Check(Body(Release("v1.0.0.1")), "1.0.0.1"));
        Assert.IsType<UpdateResult.UpToDate>(await Check(Body(Release("v1.0.0.1")), "1.0.0.2"));
    }

    [Fact]
    public async Task Hanging_request_times_out_as_Failed()
    {
        var h = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var checker = new UpdateChecker(new HttpClient(h), TimeSpan.FromMilliseconds(50));
        var r = await checker.CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        Assert.Equal("Timed out", Assert.IsType<UpdateResult.Failed>(r).Message);
    }

    [Fact]
    public async Task User_cancel_is_Failed_Cancelled()
    {
        using var cts = new CancellationTokenSource();
        var h = new FakeHandler(async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var r = await Check(h, ct: cts.Token);
        Assert.Equal("Cancelled", Assert.IsType<UpdateResult.Failed>(r).Message);
    }

    [Fact]
    public async Task Oversized_response_is_Failed()
    {
        var big = new string('x', 2 * 1024 * 1024);
        var r = await Check(Body(Release("v9.0.0") + big));
        Assert.Equal("Response too large", Assert.IsType<UpdateResult.Failed>(r).Message);
    }
}
