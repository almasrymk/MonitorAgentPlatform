using System.Net;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class LocationsTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private static object NewLocation(string name, string code) =>
        new { name, code, city = "Cairo", country = "Egypt", addressLine = "5 Test Road", timeZone = "Africa/Cairo", contactName = "Contact Person", contactEmail = "contact@alpha.test", contactPhone = "+20 100 000 0000" };

    [Fact]
    public async Task Locations_are_listed_with_the_default_last()
    {
        using var client = App.ClientFor(World.A.ReportViewer);

        var page = await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeOkAsync<PagedResult<LocationCardDto>>();

        page.Items.Select(l => l.Name).ShouldBe(["ALPHA North", "ALPHA South", "Unassigned"]);
        page.Items[^1].IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Location_is_created_read_updated_and_deleted()
    {
        using var client = App.ClientFor(World.A.ItManager);

        var created = await (await client.PostJsonAsync("/api/v1/locations", NewLocation("Alpha East", "a-east"))).ShouldBeOkAsync<LocationDto>(HttpStatusCode.Created);
        created.Code.ShouldBe("A-EAST");
        var read = await (await client.GetAsync(new Uri($"/api/v1/locations/{created.Id}", UriKind.Relative))).ShouldBeOkAsync<LocationDto>();
        read.ContactEmail.ShouldBe("contact@alpha.test");
        var updated = await (await client.PutJsonAsync($"/api/v1/locations/{created.Id}", new { name = "Alpha East Branch", code = "A-EAST", timeZone = "Asia/Dubai" })).ShouldBeOkAsync<LocationDto>();
        updated.TimeZone.ShouldBe("Asia/Dubai");
        (await client.DeleteAsync(new Uri($"/api/v1/locations/{created.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.GetAsync(new Uri($"/api/v1/locations/{created.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
    }

    [Fact]
    public async Task Codes_are_unique_per_tenant_but_may_repeat_across_tenants()
    {
        using var a = App.ClientFor(World.A.Administrator);
        using var b = App.ClientFor(World.B.Administrator);

        await (await a.PostJsonAsync("/api/v1/locations", NewLocation("Copy", World.A.Location1.Code))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "LOCATION_CODE_TAKEN");
        (await b.PostJsonAsync("/api/v1/locations", NewLocation("Same code elsewhere", World.A.Location1.Code))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_default_location_cannot_be_deleted()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.DeleteAsync(new Uri($"/api/v1/locations/{World.A.DefaultLocation.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "LOCATION_IS_DEFAULT");
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_field_errors()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var problem = await (await client.PostJsonAsync("/api/v1/locations", new { name = "", code = "bad code!", timeZone = "Mars/Olympus" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("code", out _).ShouldBeTrue();
        errors.TryGetProperty("timeZone", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Two_updates_with_the_same_row_version_give_one_409()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var location = await (await client.GetAsync(new Uri($"/api/v1/locations/{World.A.Location1.Id}", UriKind.Relative))).ShouldBeOkAsync<LocationDto>();

        async Task<HttpResponseMessage> Update(string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/locations/{location.Id}")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { name, code = location.Code, timeZone = location.TimeZone }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{location.Version}\"");
            return await client.SendAsync(request);
        }

        (await Update("First")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await Update("Second")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Report_viewer_cannot_change_locations()
    {
        using var client = App.ClientFor(World.A.ReportViewer);

        await (await client.PostJsonAsync("/api/v1/locations", NewLocation("Nope", "NOPE"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
    }
}
