using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class LocationTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static LocationDetails Details(string name = "Cairo HQ", string code = "cairo-hq") =>
        new(name, code, "Cairo", "Egypt", "1 Sample Street", "Africa/Cairo", "Contact", "contact@acme.test", "+20 100");

    [Fact]
    public void Default_location_is_unassigned_and_cannot_be_deleted_or_closed()
    {
        var location = Location.CreateDefault(Tenant, "Africa/Cairo", Now);

        location.Name.ShouldBe("Unassigned");
        location.IsDefault.ShouldBeTrue();
        Should.Throw<DomainException>(location.EnsureCanBeDeleted).Error.Code.ShouldBe("LOCATION_IS_DEFAULT");
        Should.Throw<DomainException>(location.Close).Error.Code.ShouldBe("LOCATION_IS_DEFAULT");
    }

    [Fact]
    public void Default_location_keeps_its_code_when_renamed()
    {
        var location = Location.CreateDefault(Tenant, "Africa/Cairo", Now);

        location.Update(Details("No location yet", "OTHER"));

        location.Code.ShouldBe(Location.DefaultCode);
        location.Name.ShouldBe("No location yet");
    }

    [Fact]
    public void Create_normalises_and_keeps_the_details()
    {
        var location = Location.Create(Tenant, Details(), Now);

        location.Code.ShouldBe("CAIRO-HQ");
        location.TenantId.ShouldBe(Tenant);
        location.ContactEmail.ShouldBe("contact@acme.test");
        location.Status.ShouldBe(LocationStatus.Active);
        Should.NotThrow(location.EnsureCanBeDeleted);
    }

    [Fact]
    public void Ordinary_locations_can_be_closed_and_reopened()
    {
        var location = Location.Create(Tenant, Details(), Now);

        location.Close();
        location.Status.ShouldBe(LocationStatus.Closed);
        location.Reopen();
        location.Status.ShouldBe(LocationStatus.Active);
    }

    [Fact]
    public void Name_is_required() => Should.Throw<DomainException>(() => Location.Create(Tenant, Details(name: ""), Now));

    [Fact]
    public void Tenant_is_required() => Should.Throw<DomainException>(() => Location.Create(Guid.Empty, Details(), Now));
}
