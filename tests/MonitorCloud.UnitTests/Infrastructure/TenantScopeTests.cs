using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class TenantScopeTests
{
    [Fact]
    public void A_new_scope_is_closed()
    {
        var scope = new TenantScope();

        scope.IsUnrestricted.ShouldBeFalse();
        scope.TenantId.ShouldBeNull();
        scope.LocationScope.ShouldBeEmpty();
    }

    [Fact]
    public void RunAsSystem_is_unrestricted()
    {
        var scope = new TenantScope();
        scope.RunAsTenant(Guid.CreateVersion7());

        scope.RunAsSystem();

        scope.IsUnrestricted.ShouldBeTrue();
        scope.TenantId.ShouldBeNull();
    }

    [Fact]
    public void RunAsTenant_sets_tenant_and_locations()
    {
        var scope = new TenantScope();
        var tenant = Guid.CreateVersion7();
        var location = Guid.CreateVersion7();
        scope.RunAsSystem();

        scope.RunAsTenant(tenant, [location]);

        scope.IsUnrestricted.ShouldBeFalse();
        scope.TenantId.ShouldBe(tenant);
        scope.LocationScope.ShouldBe([location]);
    }

    [Fact]
    public void RunAsTenant_requires_a_tenant() =>
        Should.Throw<ArgumentException>(() => new TenantScope().RunAsTenant(Guid.Empty));
}
