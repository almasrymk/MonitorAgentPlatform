using MonitorCloud.Domain.Licensing;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class EntitlementRulesTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static LicensingSubscriptionFacts Sub(SubscriptionStatus status, string plan, int? endInDays, int startDaysAgo = 365, params string[] features) =>
        new(Guid.CreateVersion7(), status, plan, plan[..1] + plan[1..].ToLowerInvariant(), features.Length == 0 ? ["monitoring"] : features,
            Now.AddDays(-startDaysAgo), endInDays is null ? null : Now.AddDays(endInDays.Value));

    private static LicensingLicenseFacts Lic(string status, int? max, int active) => new(Guid.CreateVersion7(), status, max, active);

    [Fact]
    public void Active_subscription_with_the_latest_end_date_is_used()
    {
        var older = Sub(SubscriptionStatus.Active, "STARTER", 30);
        var newer = Sub(SubscriptionStatus.Active, "ENTERPRISE", 300);

        var state = EntitlementRules.Derive([older, newer], []);

        state.PlanCode.ShouldBe("ENTERPRISE");
        state.SubscriptionStatus.ShouldBe(SubscriptionStatus.Active);
        state.RenewsAt.ShouldBe(newer.EndDate);
        state.StartsAt.ShouldBe(newer.StartDate);
    }

    [Fact]
    public void Trial_counts_as_usable_and_an_open_end_is_the_latest()
    {
        var active = Sub(SubscriptionStatus.Active, "STARTER", 30);
        var trial = Sub(SubscriptionStatus.Trial, "BUSINESS", null);

        EntitlementRules.Derive([active, trial], []).PlanCode.ShouldBe("BUSINESS");
    }

    [Fact]
    public void Without_a_usable_subscription_the_most_recently_ended_one_is_used()
    {
        var expired = Sub(SubscriptionStatus.Expired, "STARTER", -10);
        var cancelledEarlier = Sub(SubscriptionStatus.Cancelled, "BUSINESS", -100);
        var suspended = Sub(SubscriptionStatus.Suspended, "PROFESSIONAL", -50);

        var state = EntitlementRules.Derive([cancelledEarlier, expired, suspended], []);

        state.PlanCode.ShouldBe("STARTER");
        state.SubscriptionStatus.ShouldBe(SubscriptionStatus.Expired);
    }

    [Fact]
    public void No_subscriptions_means_status_none_and_no_features()
    {
        var state = EntitlementRules.Derive([], [Lic("Active", 10, 2)]);

        state.SubscriptionStatus.ShouldBe(SubscriptionStatus.None);
        state.PlanCode.ShouldBeNull();
        state.Features.ShouldBeEmpty();
        state.MaxDevices.ShouldBe(10);
        state.ActiveSeats.ShouldBe(2);
    }

    [Fact]
    public void Device_limit_and_seats_sum_the_active_licences_only()
    {
        var state = EntitlementRules.Derive([Sub(SubscriptionStatus.Active, "BUSINESS", 100)], [Lic("Active", 100, 40), Lic("Active", 150, 60), Lic("Suspended", 500, 7), Lic("Revoked", 10, 1)]);

        state.MaxDevices.ShouldBe(250);
        state.ActiveSeats.ShouldBe(100);
        state.LicenseIds.Count.ShouldBe(2);
    }

    [Fact]
    public void An_unlimited_active_licence_makes_the_limit_unlimited()
    {
        var state = EntitlementRules.Derive([Sub(SubscriptionStatus.Active, "ENTERPRISE", 100)], [Lic("Active", 100, 40), Lic("Active", null, 3)]);

        state.MaxDevices.ShouldBeNull();
        state.ActiveSeats.ShouldBe(43);
    }

    [Fact]
    public void Features_are_normalised_sorted_and_unique()
    {
        var state = EntitlementRules.Derive([Sub(SubscriptionStatus.Active, "BUSINESS", 100, 365, "Monitoring", " alerts", "monitoring", "")], []);

        state.Features.ShouldBe(["alerts", "monitoring"]);
    }

    [Theory]
    [InlineData("Active", SubscriptionStatus.Active)]
    [InlineData("trial", SubscriptionStatus.Trial)]
    [InlineData("Cancelled", SubscriptionStatus.Cancelled)]
    [InlineData("unknown", SubscriptionStatus.None)]
    [InlineData(null, SubscriptionStatus.None)]
    public void Statuses_are_parsed(string? value, SubscriptionStatus expected) => EntitlementRules.ParseStatus(value).ShouldBe(expected);
}

public sealed class TenantEntitlementTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static EntitlementState State(string plan = "BUSINESS", int? max = 250, int seats = 10, int renewsInDays = 100, SubscriptionStatus status = SubscriptionStatus.Active) =>
        new(plan, plan, status, ["alerts", "monitoring"], max, seats, Now.AddYears(-1), Now.AddDays(renewsInDays), []);

    [Fact]
    public void First_apply_raises_EntitlementChangedV1()
    {
        var entitlement = TenantEntitlement.Create(Tenant);

        entitlement.Apply(State(), Now).ShouldBeTrue();

        var changed = entitlement.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntitlementChangedV1>();
        changed.PlanCode.ShouldBe("BUSINESS");
        changed.PreviousPlanCode.ShouldBeNull();
        entitlement.SyncedAt.ShouldBe(Now);
        entitlement.Has("ALERTS").ShouldBeTrue();
    }

    [Fact]
    public void The_same_state_or_a_seat_change_raises_nothing()
    {
        var entitlement = TenantEntitlement.Create(Tenant);
        entitlement.Apply(State(), Now);
        entitlement.ClearDomainEvents();

        entitlement.Apply(State(), Now.AddMinutes(1)).ShouldBeFalse();
        entitlement.Apply(State(seats: 99), Now.AddMinutes(2)).ShouldBeFalse();

        entitlement.DomainEvents.ShouldBeEmpty();
        entitlement.ActiveSeats.ShouldBe(99);
        entitlement.SyncedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Theory]
    [InlineData("ENTERPRISE", 250, 100, SubscriptionStatus.Active)]
    [InlineData("BUSINESS", 300, 100, SubscriptionStatus.Active)]
    [InlineData("BUSINESS", 250, 120, SubscriptionStatus.Active)]
    [InlineData("BUSINESS", 250, 100, SubscriptionStatus.Suspended)]
    public void Plan_limit_dates_or_status_changes_raise_one_event(string plan, int max, int renews, SubscriptionStatus status)
    {
        var entitlement = TenantEntitlement.Create(Tenant);
        entitlement.Apply(State(), Now);
        entitlement.ClearDomainEvents();

        entitlement.Apply(State(plan, max, 10, renews, status), Now).ShouldBeTrue();

        entitlement.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntitlementChangedV1>().PreviousPlanCode.ShouldBe("BUSINESS");
    }

    [Fact]
    public void Expired_and_expiring_soon_are_derived()
    {
        var entitlement = TenantEntitlement.Create(Tenant);
        entitlement.Apply(State(renewsInDays: 20), Now);

        entitlement.IsExpiringSoon(Now, TimeSpan.FromDays(30)).ShouldBeTrue();
        entitlement.IsExpiringSoon(Now, TimeSpan.FromDays(10)).ShouldBeFalse();
        entitlement.IsExpired.ShouldBeFalse();

        entitlement.Apply(State(status: SubscriptionStatus.Expired, renewsInDays: 20), Now);
        entitlement.IsExpired.ShouldBeTrue();
        entitlement.IsExpiringSoon(Now, TimeSpan.FromDays(30)).ShouldBeFalse();
    }

    [Fact]
    public void Sync_errors_are_kept_short_and_cleared_by_a_success()
    {
        var entitlement = TenantEntitlement.Create(Tenant);

        entitlement.RecordSyncError(new string('e', 900), Now);
        entitlement.SyncError!.Length.ShouldBe(500);
        entitlement.Apply(State(), Now);
        entitlement.SyncError.ShouldBeNull();
    }
}

public sealed class DeviceLicenseAndSyncStateTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    [Fact]
    public void Licence_is_renewed_unlicensed_and_tracks_the_grace_period()
    {
        var license = DeviceLicense.Licensed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "LIC-1", "token", "kid", Now.AddHours(24), Now.AddDays(8), Now);
        license.State.ShouldBe(DeviceLicenseState.Licensed);

        license.Unlicense("LIC_REVOKED", Now.AddDays(1));
        license.State.ShouldBe(DeviceLicenseState.Unlicensed);
        license.UnlicensedSince.ShouldBe(Now.AddDays(1));
        license.Token.ShouldBeNull();
        license.IsInGrace(Now.AddDays(10), TimeSpan.FromDays(14)).ShouldBeTrue();
        license.IsInGrace(Now.AddDays(16), TimeSpan.FromDays(14)).ShouldBeFalse();

        license.Unlicense("LIC_EXPIRED", Now.AddDays(2));
        license.UnlicensedSince.ShouldBe(Now.AddDays(1));

        license.Renew("token2", "kid2", Now.AddDays(3), Now.AddDays(10), Now.AddDays(2));
        license.State.ShouldBe(DeviceLicenseState.Licensed);
        license.ReasonCode.ShouldBeNull();
        license.UnlicensedSince.ShouldBeNull();
        license.DeferCheck(Now.AddDays(5));
        license.CheckAfter.ShouldBe(Now.AddDays(5));
    }

    [Fact]
    public void Sync_state_counts_failures_and_knows_when_a_full_reconcile_is_due()
    {
        var state = LicensingSyncState.Create();
        state.FullReconcileDue(Now, TimeSpan.FromHours(24)).ShouldBeTrue();

        state.Failed("LICENSING_UNAVAILABLE", Now);
        state.Failed(new string('x', 2000), Now);
        state.ConsecutiveFailures.ShouldBe(2);
        state.LastError!.Length.ShouldBe(1000);

        state.Succeeded("cursor-1", Now.AddMinutes(1), fullReconcile: true);
        state.ConsecutiveFailures.ShouldBe(0);
        state.Cursor.ShouldBe("cursor-1");
        state.FullReconcileDue(Now.AddHours(23), TimeSpan.FromHours(24)).ShouldBeFalse();
        state.FullReconcileDue(Now.AddHours(25), TimeSpan.FromHours(24)).ShouldBeTrue();
    }
}
