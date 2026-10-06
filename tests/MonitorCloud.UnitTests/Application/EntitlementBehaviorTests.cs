using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared.Builders;
using NSubstitute;

namespace MonitorCloud.UnitTests.Application;

public sealed class EntitlementBehaviorTests
{
    private static readonly Guid TenantA = Guid.CreateVersion7();

    private static IEntitlementReader Reader(bool expired, params string[] features)
    {
        var reader = Substitute.For<IEntitlementReader>();
        reader.GetAsync(TenantA, Arg.Any<CancellationToken>()).Returns(new EntitlementSnapshot(features, expired));
        return reader;
    }

    private static async Task<(TResponse Response, bool Called)> Run<TRequest, TResponse>(TRequest request, TResponse success, IEntitlementReader reader, TestTenantContext? scope = null)
        where TRequest : notnull
        where TResponse : Result
    {
        var called = false;
        var behavior = new EntitlementBehavior<TRequest, TResponse>(scope ?? TestTenantContext.ForTenant(TenantA), reader);
        var response = await behavior.Handle(request, _ =>
        {
            called = true;
            return Task.FromResult(success);
        }, CancellationToken.None);
        return (response, called);
    }

    [Fact]
    public async Task Requests_without_a_feature_are_not_checked()
    {
        var reader = Reader(expired: true);

        var (response, called) = await Run(new RenameDeviceCommand("a"), Result<string>.Ok("a"), reader);

        response.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
        await reader.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Missing_feature_returns_FEATURE_NOT_ENTITLED()
    {
        var (response, called) = await Run(new EditMonitorPointCommand(), Result.Success(), Reader(false, "reports.basic"));

        response.Error!.Code.ShouldBe("FEATURE_NOT_ENTITLED");
        response.Error.Message.ShouldContain("monitoring.advanced");
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task Entitled_feature_passes()
    {
        var (response, called) = await Run(new EditMonitorPointCommand(), Result.Success(), Reader(false, "MONITORING.ADVANCED"));

        response.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task Expired_subscription_refuses_feature_gated_commands()
    {
        var (response, _) = await Run(new EditMonitorPointCommand(), Result.Success(), Reader(true, "monitoring.advanced"));

        response.Error!.Code.ShouldBe("ENTITLEMENT_EXPIRED");
    }

    [Fact]
    public async Task Expired_subscription_still_allows_feature_gated_queries()
    {
        var (response, _) = await Run(new ReadReportQuery(), Result<int>.Ok(1), Reader(true, "reports.basic"));

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_scope_without_a_workspace_is_not_limited_by_a_plan()
    {
        var reader = Reader(false);

        var (response, _) = await Run(new EditMonitorPointCommand(), Result.Success(), reader, TestTenantContext.Platform());

        response.IsSuccess.ShouldBeTrue();
        await reader.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }
}
