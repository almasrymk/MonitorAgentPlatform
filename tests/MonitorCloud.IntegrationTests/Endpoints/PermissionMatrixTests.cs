using System.Net;
using System.Text.Json;
using MonitorCloud.Domain.Identity;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>For every endpoint and every role: allowed roles get a non-403 answer, others get 403 <c>AUTH_FORBIDDEN</c>.</summary>
[Collection(SqlCollection.Name)]
public sealed class PermissionMatrixTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    [Fact]
    public void Every_business_endpoint_has_a_row_in_the_test_data()
    {
        var missing = BusinessEndpoints.Where(e => !EndpointPermissions.Rows.ContainsKey(e.Key)).Select(e => e.Key).ToList();
        var stale = EndpointPermissions.Rows.Keys.Where(k => BusinessEndpoints.All(e => e.Key != k)).ToList();

        missing.ShouldBeEmpty("Add the endpoint to EndpointPermissions.");
        stale.ShouldBeEmpty("Remove rows for endpoints that no longer exist.");
    }

    [Fact]
    public void Production_role_map_equals_the_matrix_of_03()
    {
        foreach (var role in Roles.All)
        {
            Roles.PermissionsOf(role).Order(StringComparer.Ordinal)
                .ShouldBe(PermissionMatrix.PermissionsOf(role).Order(StringComparer.Ordinal), role);
        }
    }

    [Fact]
    public async Task Each_role_is_allowed_or_forbidden_as_the_matrix_says()
    {
        var failures = new List<string>();
        foreach (var role in PermissionMatrix.Roles)
        {
            var user = World.UserOf(role, World.A);
            var platform = Roles.IsPlatformRole(role);
            foreach (var endpoint in BusinessEndpoints.Where(e => EndpointPermissions.Rows[e.Key] is not (EndpointPermissions.Anonymous or EndpointPermissions.DeviceOnly)))
            {
                var permission = EndpointPermissions.Rows[endpoint.Key];
                // Platform roles call tenant endpoints inside the workspace of customer A.
                using var client = App.ClientFor(user, platform && endpoint.IsTenant ? World.A.Id : null);
                using var request = Request(endpoint, Guid.CreateVersion7());
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                var allowed = permission == EndpointPermissions.AnyUser
                    || (PermissionMatrix.Allows(role, permission) && (!endpoint.IsPlatform || platform));

                if (allowed && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                    failures.Add($"{role} {endpoint}: expected access, got {(int)response.StatusCode} {body}");
                if (!allowed && (response.StatusCode != HttpStatusCode.Forbidden || Code(body) != "AUTH_FORBIDDEN"))
                    failures.Add($"{role} {endpoint}: expected 403 AUTH_FORBIDDEN, got {(int)response.StatusCode} {body}");
            }
        }

        failures.ShouldBeEmpty();
    }

    private static string? Code(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}
