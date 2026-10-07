using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.ContractTests;

public sealed class FakeContractFixture : IContractFixture
{
    private readonly FakeLicensingStore _store = new();
    private readonly TestClock _clock = new();

    public FakeContractFixture()
    {
        var now = _clock.GetUtcNow();
        var data = TestLicensing.NewData();
        CustomerId = Guid.CreateVersion7();
        ActiveKey = data.AddCustomer(CustomerId, "Contract Customer", "ENTERPRISE", now).ProductKey;
        LimitedKey = data.AddCustomer(Guid.CreateVersion7(), "Limited Customer", "STARTER", now, maxActivations: 2).ProductKey;
        SuspendedKey = data.AddCustomer(Guid.CreateVersion7(), "Suspended Customer", "STARTER", now, licenseStatus: "Suspended").ProductKey;
        ExpiredKey = data.AddCustomer(Guid.CreateVersion7(), "Expired Customer", "STARTER", now, subscriptionStatus: "Expired", renewsInDays: -1).ProductKey;
        _store.Load(data);
        Gateway = new FakeLicensingGateway(_store, _clock);
    }

    public ILicensingGateway Gateway { get; }
    public string ActiveKey { get; }
    public string? LimitedKey { get; }
    public string? SuspendedKey { get; }
    public string? ExpiredKey { get; }
    public Guid CustomerId { get; }
    public IReadOnlyList<string> PlanCodes { get; } = ["STARTER", "PROFESSIONAL", "BUSINESS", "ENTERPRISE"];
    public bool CanSimulateRateLimit => true;

    public void SimulateRateLimit() => _store.RateLimitNextCalls = 1;

    public void AppendChange()
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        _store.Append("SubscriptionRenewedV1", _clock.GetUtcNow(), CustomerId, null, null);
    }

    public string NewDeviceId() => $"contract-{Guid.NewGuid():N}";
}

/// <summary>The contract against <see cref="FakeLicensingGateway"/>: always runs.</summary>
public sealed class FakeLicensingContractTests : LicensingGatewayContract, IClassFixture<FakeContractFixture>
{
    private readonly FakeContractFixture _fixture;

    public FakeLicensingContractTests(FakeContractFixture fixture) => _fixture = fixture;

    protected override IContractFixture Fixture => _fixture;
}
