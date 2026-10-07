using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>A feature-gated command used to check the entitlement pipeline end to end.</summary>
[RequirePermission(MonitorCloud.Domain.Identity.Permissions.DevicesManage)]
[RequiresFeature("monitorpoints")]
public sealed record GatedTestCommand : ICommand;

public sealed class GatedTestCommandHandler : ICommandHandler<GatedTestCommand>
{
    public Task<Result> Handle(GatedTestCommand request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
}

public sealed class LicensingApp(SqlServerFixture sql) : TestApp(sql)
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GatedTestCommand).Assembly));
}

[Collection(SqlCollection.Name)]
public sealed class LicensingTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly LicensingApp _app = new(sql);
    private MonitorCloud.TestShared.Builders.TestWorld _world = null!;

    private FakeLicensingStore Store => _app.Services.GetRequiredService<FakeLicensingStore>();

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
        _world = await MonitorCloud.TestShared.Builders.TestWorld.CreateAsync(_app);
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    private Task<SyncOutcome> SyncChangesAsync() => _app.InSystemScopeAsync(sp => sp.GetRequiredService<LicensingSyncService>().SyncChangesAsync(CancellationToken.None));

    private Task<SyncOutcome> ReconcileAsync() => _app.InSystemScopeAsync(sp => sp.GetRequiredService<LicensingSyncService>().ReconcileAllAsync(CancellationToken.None));

    private Task<int> EntitlementEventsAsync(Guid tenantId) =>
        _app.InDbAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(EntitlementChangedV1).FullName && m.TenantId == tenantId));

    [Fact]
    public async Task Subscription_shows_the_cached_entitlement()
    {
        using var client = _app.ClientFor(_world.A.Administrator);

        var subscription = await (await client.GetAsync(new Uri("/api/v1/subscription", UriKind.Relative))).ShouldBeOkAsync<SubscriptionDto>();

        subscription.PlanCode.ShouldBe("ENTERPRISE");
        subscription.Status.ShouldBe("Active");
        subscription.DeviceLimit.ShouldBe(500);
        subscription.DevicesUsed.ShouldBe(3);
        subscription.DaysRemaining.ShouldBe(200);
        subscription.Features.ShouldContain("remote.actions");
        subscription.Stale.ShouldBeFalse();
    }

    [Fact]
    public async Task Plan_change_is_reflected_after_one_sync_with_exactly_one_event()
    {
        var before = await EntitlementEventsAsync(_world.A.Id);
        Store.ChangePlan(_world.LicensingCustomerOf(_world.A), "PROFESSIONAL", _app.Clock.GetUtcNow());

        var outcome = await SyncChangesAsync();
        await SyncChangesAsync();

        outcome.Succeeded.ShouldBeTrue();
        outcome.TenantsChanged.ShouldBe(1);
        (await EntitlementEventsAsync(_world.A.Id)).ShouldBe(before + 1);
        using var client = _app.ClientFor(_world.A.Administrator);
        var subscription = await (await client.GetAsync(new Uri("/api/v1/subscription", UriKind.Relative))).ShouldBeOkAsync<SubscriptionDto>();
        subscription.PlanCode.ShouldBe("PROFESSIONAL");
        subscription.DeviceLimit.ShouldBe(200);
    }

    [Fact]
    public async Task Suspending_the_subscription_is_reflected()
    {
        Store.SetSubscriptionStatus(_world.LicensingCustomerOf(_world.B), "Suspended", _app.Clock.GetUtcNow());

        await SyncChangesAsync();

        using var client = _app.ClientFor(_world.B.Administrator);
        (await (await client.GetAsync(new Uri("/api/v1/subscription", UriKind.Relative))).ShouldBeOkAsync<SubscriptionDto>()).Status.ShouldBe("Suspended");
    }

    [Fact]
    public async Task A_feature_gated_command_is_refused_for_a_Starter_tenant()
    {
        async Task<Result> SendAs(MonitorCloud.TestShared.Builders.TestTenant tenant)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsTenant(tenant.Id);
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GatedTestCommand());
        }

        var starter = await SendAs(_world.B);
        var enterprise = await SendAs(_world.A);

        starter.Error!.Code.ShouldBe("FEATURE_NOT_ENTITLED");
        starter.Error.Details!["feature"].ShouldBe("monitorpoints");
        enterprise.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_outage_keeps_the_cached_entitlements_and_degrades_health_after_five_failures()
    {
        Store.Unavailable = true;
        for (var i = 0; i < 5; i++)
            (await SyncChangesAsync()).Succeeded.ShouldBeFalse();

        using var client = _app.ClientFor(_world.A.Administrator);
        (await (await client.GetAsync(new Uri("/api/v1/subscription", UriKind.Relative))).ShouldBeOkAsync<SubscriptionDto>()).PlanCode.ShouldBe("ENTERPRISE");
        using var anonymous = _app.CreateClient();
        var health = await anonymous.GetStringAsync(new Uri("/health/ready", UriKind.Relative));
        health.ShouldContain("\"licensing\":{\"status\":\"Degraded\"");
        using var admin = _app.ClientFor(_world.PlatformAdmin);
        var status = await (await admin.GetAsync(new Uri("/api/v1/platform/licensing/status", UriKind.Relative))).ShouldBeOkAsync<LicensingStatusDto>();
        status.ConsecutiveFailures.ShouldBe(5);
        status.Degraded.ShouldBeTrue();
        status.LastError.ShouldNotBeNull().ShouldStartWith("LICENSING_UNAVAILABLE");

        Store.Unavailable = false;
        (await SyncChangesAsync()).Succeeded.ShouldBeTrue();
        (await (await admin.GetAsync(new Uri("/api/v1/platform/licensing/status", UriKind.Relative))).ShouldBeOkAsync<LicensingStatusDto>()).ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public async Task Manual_sync_reconciles_every_linked_tenant()
    {
        using var admin = _app.ClientFor(_world.PlatformAdmin);

        var outcome = await (await admin.PostAsync(new Uri("/api/v1/platform/licensing/sync", UriKind.Relative), null)).ShouldBeOkAsync<SyncOutcome>();

        outcome.Succeeded.ShouldBeTrue();
        outcome.TenantsChecked.ShouldBe(2);
        outcome.TenantsChanged.ShouldBe(0);
    }

    [Fact]
    public async Task Manual_sync_during_an_outage_answers_503()
    {
        Store.Unavailable = true;
        using var admin = _app.ClientFor(_world.PlatformAdmin);

        await (await admin.PostAsync(new Uri("/api/v1/platform/licensing/sync", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "LICENSING_UNAVAILABLE");
    }

    [Fact]
    public async Task Plans_list_comes_from_Licensing_with_customer_counts()
    {
        using var admin = _app.ClientFor(_world.PlatformAdmin);

        var plans = await (await admin.GetAsync(new Uri("/api/v1/platform/plans", UriKind.Relative))).ShouldBeOkAsync<List<PlanDto>>();

        plans.Select(p => p.Code).ShouldBe(["STARTER", "PROFESSIONAL", "BUSINESS", "ENTERPRISE"]);
        plans.Single(p => p.Code == "ENTERPRISE").Customers.ShouldBe(1);
        plans.Single(p => p.Code == "STARTER").Customers.ShouldBe(1);
        plans.Single(p => p.Code == "BUSINESS").Customers.ShouldBe(0);
    }

    [Fact]
    public async Task Customer_cards_show_plan_licence_usage_and_renewal_and_filter_by_plan_and_expiry()
    {
        Store.Mutate(d => d.Subscriptions.Single(s => s.CustomerId == _world.LicensingCustomerOf(_world.B)).EndDate = _app.Clock.GetUtcNow().AddDays(10));
        Store.Append("SubscriptionRenewedV1", _app.Clock.GetUtcNow(), _world.LicensingCustomerOf(_world.B), null, null);
        await SyncChangesAsync();
        using var admin = _app.ClientFor(_world.PlatformAdmin);

        var all = await (await admin.GetAsync(new Uri("/api/v1/platform/tenants", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        var enterprise = await (await admin.GetAsync(new Uri("/api/v1/platform/tenants?plan=ENTERPRISE", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        var expiring = await (await admin.GetAsync(new Uri("/api/v1/platform/tenants?subscriptionStatus=Expiring", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        var summary = await (await admin.GetAsync(new Uri("/api/v1/platform/tenants/summary", UriKind.Relative))).ShouldBeOkAsync<TenantsSummaryDto>();

        var alpha = all.Items.Single(t => t.Id == _world.A.Id);
        alpha.PlanName.ShouldBe("Enterprise");
        alpha.LicensesUsed.ShouldBe(3);
        alpha.LicenseLimit.ShouldBe(500);
        alpha.NextRenewal.ShouldNotBeNull();
        enterprise.Items.ShouldHaveSingleItem().Id.ShouldBe(_world.A.Id);
        expiring.Items.ShouldHaveSingleItem().Id.ShouldBe(_world.B.Id);
        expiring.Items[0].ExpiringSoon.ShouldBeTrue();
        summary.ExpiringSoon.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_Licensing_customer_is_provisioned_once_with_location_and_entitlement()
    {
        var customer = Guid.CreateVersion7();
        Store.Mutate(d => d.AddCustomer(customer, "Alpha", "BUSINESS", _app.Clock.GetUtcNow()));

        var first = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<ITenantProvisioning>().EnsureTenantAsync(customer, "Alpha", "Egypt", CancellationToken.None));
        var second = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<ITenantProvisioning>().EnsureTenantAsync(customer, "Alpha", "Egypt", CancellationToken.None));
        await _app.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);

        first.Created.ShouldBeTrue();
        second.Created.ShouldBeFalse();
        second.TenantId.ShouldBe(first.TenantId);
        var tenant = await _app.InDbAsync(db => db.Set<Tenant>().SingleAsync(t => t.Id == first.TenantId));
        tenant.Code.ShouldBe("ALPHA-2");
        (await _app.InDbAsync(db => db.Set<Location>().CountAsync(l => l.TenantId == tenant.Id && l.IsDefault))).ShouldBe(1);
        (await _app.InDbAsync(db => db.Set<TenantEntitlement>().SingleAsync(e => e.TenantId == tenant.Id))).PlanCode.ShouldBe("BUSINESS");
    }

    [Fact]
    public async Task Linking_a_new_tenant_on_create_loads_its_entitlement()
    {
        var customer = Guid.CreateVersion7();
        Store.Mutate(d => d.AddCustomer(customer, "Gamma", "STARTER", _app.Clock.GetUtcNow()));
        using var admin = _app.ClientFor(_world.PlatformAdmin);

        var created = await (await admin.PostJsonAsync("/api/v1/platform/tenants", new { name = "Gamma Test Company", country = "Egypt", city = "Cairo", licensingCustomerId = customer }))
            .ShouldBeOkAsync<TenantDto>(HttpStatusCode.Created);
        await _app.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);

        (await _app.InDbAsync(db => db.Set<TenantEntitlement>().SingleAsync(e => e.TenantId == created.Id))).PlanCode.ShouldBe("STARTER");
    }

    [Fact]
    public async Task Tenant_without_an_entitlement_has_no_features_and_status_none()
    {
        using var admin = _app.ClientFor(_world.PlatformAdmin);
        var created = await (await admin.PostJsonAsync("/api/v1/platform/tenants", new { name = "Unlinked Test Company", country = "Egypt", city = "Cairo" })).ShouldBeOkAsync<TenantDto>(HttpStatusCode.Created);
        using var workspace = _app.ClientFor(_world.PlatformAdmin, created.Id);

        var subscription = await (await workspace.GetAsync(new Uri("/api/v1/subscription", UriKind.Relative))).ShouldBeOkAsync<SubscriptionDto>();

        subscription.Status.ShouldBe("None");
        subscription.Features.ShouldBeEmpty();
    }

    [Fact]
    public async Task Full_reconcile_marks_unknown_customers_without_failing()
    {
        Store.Mutate(d => d.Customers.RemoveAll(c => c.Id == _world.LicensingCustomerOf(_world.B)));

        var outcome = await ReconcileAsync();

        outcome.Succeeded.ShouldBeTrue();
        (await _app.InDbAsync(db => db.Set<TenantEntitlement>().SingleAsync(e => e.TenantId == _world.B.Id))).SyncError.ShouldBe("LICENSING_CUSTOMER_NOT_FOUND");
    }
}
