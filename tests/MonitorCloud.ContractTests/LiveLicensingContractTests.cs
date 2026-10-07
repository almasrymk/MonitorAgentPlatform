using Microsoft.Extensions.Options;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Infrastructure.Licensing.Http;

namespace MonitorCloud.ContractTests;

/// <summary>
/// The same contract against a live Licensing Platform with LP-1..LP-5 (CI job <c>licensing-contract</c>). Configured by
/// <c>LICENSING_CONTRACT_URL</c>, <c>_CLIENT_ID</c>, <c>_CLIENT_SECRET</c>, <c>_PRODUCT_KEY</c>, <c>_CUSTOMER_ID</c> and
/// optionally <c>_LIMITED_KEY</c>, <c>_SUSPENDED_KEY</c>, <c>_EXPIRED_KEY</c>, <c>_PRODUCT_CODE</c>.
/// Without them every test of this class reports "not configured" and passes.
/// </summary>
public sealed class LiveContractFixture : IContractFixture, IDisposable
{
    private readonly HttpClient? _http;
    private readonly LicensingTokenCache? _tokens;

    public LiveContractFixture()
    {
        var url = Env("URL");
        Configured = url is not null && Env("CLIENT_ID") is not null && Env("CLIENT_SECRET") is not null && Env("PRODUCT_KEY") is not null && Env("CUSTOMER_ID") is not null;
        if (!Configured)
        {
            Gateway = null!;
            ActiveKey = string.Empty;
            return;
        }

        var options = Options.Create(new LicensingSettings
        {
            Mode = "Live", BaseUrl = url!, ClientId = Env("CLIENT_ID")!, ClientSecret = Env("CLIENT_SECRET")!, ProductCode = Env("PRODUCT_CODE") ?? "000001",
        });
        var baseAddress = new Uri(url!.TrimEnd('/') + "/");
        _http = new HttpClient { BaseAddress = baseAddress };
        _tokens = new LicensingTokenCache(new SimpleFactory(baseAddress), options, TimeProvider.System);
        Gateway = new HttpLicensingGateway(_http, _tokens, options);
        ActiveKey = Env("PRODUCT_KEY")!;
        LimitedKey = Env("LIMITED_KEY");
        SuspendedKey = Env("SUSPENDED_KEY");
        ExpiredKey = Env("EXPIRED_KEY");
        CustomerId = Guid.Parse(Env("CUSTOMER_ID")!);
    }

    public bool Configured { get; }
    public ILicensingGateway Gateway { get; }
    public string ActiveKey { get; }
    public string? LimitedKey { get; }
    public string? SuspendedKey { get; }
    public string? ExpiredKey { get; }
    public Guid CustomerId { get; }
    public IReadOnlyList<string> PlanCodes { get; } = ["STARTER", "PROFESSIONAL", "BUSINESS", "ENTERPRISE"];
    public bool CanSimulateRateLimit => false;

    public void SimulateRateLimit()
    {
    }

    public void AppendChange()
    {
    }

    public string NewDeviceId() => $"contract-{Guid.NewGuid():N}";

    public void Dispose()
    {
        _http?.Dispose();
        _tokens?.Dispose();
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable($"LICENSING_CONTRACT_{name}") is { Length: > 0 } value ? value : null;

    private sealed class SimpleFactory(Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseAddress };
    }
}

public sealed class LiveLicensingContractTests : LicensingGatewayContract, IClassFixture<LiveContractFixture>
{
    private readonly LiveContractFixture _fixture;

    public LiveLicensingContractTests(LiveContractFixture fixture) => _fixture = fixture;

    protected override IContractFixture Fixture => _fixture;

    protected override void Ready() =>
        Skip.If(!_fixture.Configured, "Live Licensing contract not configured (LICENSING_CONTRACT_* variables).");
}
