using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static Tenant NewTenant(string? code = null) =>
        Tenant.Create("Acme Corporation", code, "Egypt", "Cairo", null, new DateOnly(2024, 1, 1), null, Now);

    [Fact]
    public void Create_sets_defaults_and_raises_TenantCreatedV1()
    {
        var tenant = NewTenant();

        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.Code.ShouldBe("ACME-CORPORATION");
        tenant.TimeZone.ShouldBe("Africa/Cairo");
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantCreatedV1>().TenantId.ShouldBe(tenant.Id);
    }

    [Theory]
    [InlineData("acme", "ACME")]
    [InlineData(" Nile_1 ", "NILE_1")]
    public void Codes_are_upper_case(string code, string expected) => NewTenant(code).Code.ShouldBe(expected);

    [Fact]
    public void Codes_with_invalid_characters_are_rejected() => Should.Throw<DomainException>(() => NewTenant("acme corp"));

    [Theory]
    [InlineData("Acme Corporation", "ACME-CORPORATION")]
    [InlineData("  Nile & Co.  ", "NILE-CO")]
    [InlineData("!!!", "CUSTOMER")]
    public void Code_from_name_is_a_slug(string name, string expected) => Tenant.CodeFromName(name).ShouldBe(expected);

    [Fact]
    public void Code_from_a_long_name_is_trimmed_to_32_characters() =>
        Tenant.CodeFromName(new string('A', 40)).Length.ShouldBe(32);

    [Fact]
    public void Suspend_then_resume()
    {
        var tenant = NewTenant();
        tenant.ClearDomainEvents();

        tenant.Suspend("Unpaid invoice", Now);
        tenant.Status.ShouldBe(TenantStatus.Suspended);
        tenant.SuspensionReason.ShouldBe("Unpaid invoice");
        tenant.SuspendedAt.ShouldBe(Now);

        tenant.Resume(Now.AddDays(1));
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.SuspensionReason.ShouldBeNull();
        tenant.DomainEvents.Select(e => e.GetType()).ShouldBe([typeof(TenantSuspendedV1), typeof(TenantResumedV1)]);
    }

    [Fact]
    public void Invalid_transitions_are_refused()
    {
        var tenant = NewTenant();

        Should.Throw<DomainException>(() => tenant.Resume(Now)).Error.Code.ShouldBe("TENANT_INVALID_TRANSITION");
        tenant.Suspend("Reason", Now);
        Should.Throw<DomainException>(() => tenant.Suspend("Again", Now));
        tenant.Archive("Ended", Now);
        Should.Throw<DomainException>(() => tenant.Archive("Again", Now));
        Should.Throw<DomainException>(() => tenant.Resume(Now));
        Should.Throw<DomainException>(() => tenant.Update("X", "Egypt", "Cairo", "Africa/Cairo"));
    }

    [Fact]
    public void Archive_raises_TenantArchivedV1()
    {
        var tenant = NewTenant();
        tenant.ClearDomainEvents();

        tenant.Archive("Contract ended", Now);

        tenant.ArchivedAt.ShouldBe(Now);
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantArchivedV1>().Reason.ShouldBe("Contract ended");
    }

    [Fact]
    public void Suspend_requires_a_reason() => Should.Throw<DomainException>(() => NewTenant().Suspend(" ", Now));

    [Fact]
    public void Update_and_link_licensing_customer()
    {
        var tenant = NewTenant();
        var customer = Guid.CreateVersion7();

        tenant.Update("Acme Corp", "UAE", "Dubai", "Asia/Dubai");
        tenant.LinkLicensingCustomer(customer);

        tenant.Name.ShouldBe("Acme Corp");
        tenant.TimeZone.ShouldBe("Asia/Dubai");
        tenant.LicensingCustomerId.ShouldBe(customer);
    }

    [Fact]
    public void Fixed_ids_are_accepted_for_seeding()
    {
        var id = Guid.CreateVersion7();

        Tenant.Create("A", "A", "Egypt", "Cairo", null, new DateOnly(2024, 1, 1), null, Now, id).Id.ShouldBe(id);
    }
}
