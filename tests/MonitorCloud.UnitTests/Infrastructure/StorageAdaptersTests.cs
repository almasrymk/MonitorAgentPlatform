using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitorCloud.Infrastructure.Storage;
using MonitorCloud.TestShared;
using NSubstitute;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class StorageAdaptersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "monitorcloud-unit", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private IOptions<StorageOptions> Options(string? chromium = null) => Microsoft.Extensions.Options.Options.Create(new StorageOptions { Root = _root, ChromiumPath = chromium });

    private static IHostEnvironment Environment()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        return environment;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task Files_are_written_once_and_read_back()
    {
        var storage = new FileMediaStorage(Options(), Environment());

        await storage.WriteAsync("tenant/abc", "hello"u8.ToArray(), CancellationToken.None);
        await using (var stream = await storage.OpenAsync("tenant/abc", CancellationToken.None))
        {
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync()).ShouldBe("hello");
        }

        await Should.ThrowAsync<IOException>(() => storage.WriteAsync("tenant/abc", "again"u8.ToArray(), CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("tenant/../../outside")]
    public async Task Paths_outside_the_media_folder_are_refused(string path)
    {
        var storage = new FileMediaStorage(Options(), Environment());

        await Should.ThrowAsync<InvalidOperationException>(() => storage.WriteAsync(path, "x"u8.ToArray(), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(() => storage.OpenAsync(path, CancellationToken.None));
    }

    [Fact]
    public void Secrets_round_trip_and_are_not_stored_in_plain_text()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var stored = protector.Protect("TEST-ONLY-secret");

        stored.ShouldNotContain("TEST-ONLY-secret");
        protector.Unprotect(stored).ShouldBe("TEST-ONLY-secret");
        Should.Throw<CryptographicException>(() => new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Unprotect(stored));
    }

    [Fact]
    public void Pdf_is_unavailable_when_the_configured_browser_is_missing()
    {
        var renderer = new ChromiumPdfRenderer(Options(Path.Combine(_root, "no-browser.exe")), Environment(), NullLogger<ChromiumPdfRenderer>.Instance);

        renderer.IsAvailable.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => renderer.RenderAsync("<p/>", CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Webhooks_are_signed_with_the_timestamp_and_body()
    {
        var clock = new TestClock();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sender = new HttpWebhookSender(new Factory(handler), clock);

        var result = await sender.SendAsync("https://hooks.alpha.test/x", "TEST-ONLY-hook", """{"type":"test"}""", CancellationToken.None);

        result.ShouldBe(new MonitorCloud.Application.Abstractions.Storage.WebhookResult(true, 204, null));
        var timestamp = handler.Last!.Headers.GetValues("X-Monitor-Timestamp").Single();
        timestamp.ShouldBe(clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("TEST-ONLY-hook"), Encoding.UTF8.GetBytes($"{timestamp}.{handler.Body}"))).ToLowerInvariant();
        handler.Last.Headers.GetValues("X-Monitor-Signature").Single().ShouldBe($"sha256={expected}");
        handler.Last.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Webhook_failures_are_reported_not_thrown()
    {
        var clock = new TestClock();
        var unsigned = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var result = await new HttpWebhookSender(new Factory(unsigned), clock).SendAsync("https://hooks.alpha.test/x", null, "{}", CancellationToken.None);
        result.ShouldBe(new MonitorCloud.Application.Abstractions.Storage.WebhookResult(false, 502, "HTTP 502"));
        unsigned.Last!.Headers.Contains("X-Monitor-Signature").ShouldBeFalse();

        var broken = new StubHandler(_ => throw new HttpRequestException("No such host"));
        (await new HttpWebhookSender(new Factory(broken), clock).SendAsync("https://nowhere.test", null, "{}", CancellationToken.None))
            .ShouldBe(new MonitorCloud.Application.Abstractions.Storage.WebhookResult(false, null, "No such host"));

        var slow = new StubHandler(_ => throw new TaskCanceledException());
        (await new HttpWebhookSender(new Factory(slow), clock).SendAsync("https://slow.test", null, "{}", CancellationToken.None)).Error.ShouldBe("Timed out.");
    }
}
