using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>Consistency checks between requests, endpoints and the permission matrix.</summary>
[Collection(SqlCollection.Name)]
public sealed class AuthorizationSuitesTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    [Fact]
    public void Permission_matrix_covers_the_permissions_of_03()
    {
        PermissionMatrix.Permissions.Count.ShouldBe(29);
        PermissionMatrix.Allows(PermissionMatrix.PlatformAdmin, "audit.read").ShouldBeTrue();
        PermissionMatrix.Allows(PermissionMatrix.ReportViewer, "reports.generate").ShouldBeTrue();
        PermissionMatrix.Allows(PermissionMatrix.Technician, "reports.generate").ShouldBeFalse();
        PermissionMatrix.Allows(PermissionMatrix.PlatformSupport, "devices.manage").ShouldBeFalse();
    }

    [Fact]
    public void Every_permission_named_by_a_request_is_in_the_matrix()
    {
        var used = typeof(MonitorCloud.Application.DependencyInjection).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>())
            .Select(a => a.Permission)
            .Distinct(StringComparer.Ordinal);

        used.ShouldAllBe(p => PermissionMatrix.Permissions.Contains(p));
    }

    [Fact]
    public void Platform_routes_live_under_api_v1_platform()
    {
        var offenders = BusinessEndpoints
            .Where(e => e.Route.Contains("/platform", StringComparison.OrdinalIgnoreCase) && !e.IsPlatform)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Business_endpoints_are_versioned()
    {
        var offenders = BusinessEndpoints
            .Where(e => !e.Route.StartsWith("/api/v1/", StringComparison.Ordinal) && !e.IsAgent)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void No_tenant_endpoint_accepts_a_tenant_id_parameter()
    {
        var offenders = BusinessEndpoints
            .Where(e => !e.IsPlatform)
            .Where(e => e.Metadata.OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
                .Any(d => d.Parameters.Any(p => p.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase)
                    || (p.ParameterType.GetProperty("TenantId") is not null))))
            .Select(e => e.Key)
            .ToList();

        offenders.ShouldBeEmpty();
    }
}
