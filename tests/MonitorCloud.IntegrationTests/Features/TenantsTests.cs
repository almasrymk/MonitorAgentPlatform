using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class TenantsTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private Task DispatchOutboxAsync() => App.Services.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None);

    [Fact]
    public async Task Creating_a_customer_creates_its_default_location_through_the_outbox()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        var created = await (await client.PostJsonAsync("/api/v1/platform/tenants", new { name = "Gamma Test Company", country = "Egypt", city = "Cairo", timeZone = "Africa/Cairo" }))
            .ShouldBeOkAsync<TenantDto>(HttpStatusCode.Created);
        created.Code.ShouldBe("GAMMA-TEST-COMPANY");
        created.Status.ShouldBe("Active");
        await DispatchOutboxAsync();

        var locations = await App.InDbAsync(db => db.Set<Location>().Where(l => l.TenantId == created.Id).ToListAsync());
        locations.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
        locations[0].Name.ShouldBe("Unassigned");
    }

    [Fact]
    public async Task Customer_codes_are_unique()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        await (await client.PostJsonAsync("/api/v1/platform/tenants", new { name = "Another", code = "alpha", country = "Egypt", city = "Cairo" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "TENANT_CODE_TAKEN");
    }

    [Fact]
    public async Task List_and_summary_show_customers_and_hide_archived_ones()
    {
        using var client = App.ClientFor(World.PlatformAdmin);
        (await client.PostJsonAsync($"/api/v1/platform/tenants/{World.B.Id}/archive", new { reason = "Contract ended" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await (await client.GetAsync(new Uri("/api/v1/platform/tenants", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        var archived = await (await client.GetAsync(new Uri("/api/v1/platform/tenants?status=Archived", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        var summary = await (await client.GetAsync(new Uri("/api/v1/platform/tenants/summary", UriKind.Relative))).ShouldBeOkAsync<TenantsSummaryDto>();

        list.Items.ShouldHaveSingleItem().Id.ShouldBe(World.A.Id);
        list.Items[0].Locations.ShouldBe(2);
        archived.Items.ShouldHaveSingleItem().Id.ShouldBe(World.B.Id);
        summary.ShouldBe(new TenantsSummaryDto(1, 1, 0, 0));
    }

    [Fact]
    public async Task Suspend_resume_and_archive_follow_the_transitions_and_are_audited()
    {
        using var client = App.ClientFor(World.PlatformAdmin);
        var url = $"/api/v1/platform/tenants/{World.A.Id}";

        await (await client.PostAsync(new Uri(url + "/resume", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "TENANT_INVALID_TRANSITION");
        await (await client.PostJsonAsync(url + "/suspend", new { reason = "" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        (await (await client.PostJsonAsync(url + "/suspend", new { reason = "Unpaid invoice" })).ShouldBeOkAsync<TenantDto>()).Status.ShouldBe("Suspended");
        App.Clock.Advance(TimeSpan.FromSeconds(1));
        (await (await client.PostAsync(new Uri(url + "/resume", UriKind.Relative), null)).ShouldBeOkAsync<TenantDto>()).Status.ShouldBe("Active");
        App.Clock.Advance(TimeSpan.FromSeconds(1));
        (await (await client.PostJsonAsync(url + "/archive", new { reason = "Contract ended" })).ShouldBeOkAsync<TenantDto>()).Status.ShouldBe("Archived");
        await (await client.PostJsonAsync(url + "/suspend", new { reason = "Again" })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "TENANT_INVALID_TRANSITION");

        var actions = await App.InDbAsync(db => db.Set<AuditRecord>().Where(a => a.EntityId == World.A.Id.ToString()).OrderBy(a => a.At).Select(a => a.Action).ToListAsync());
        actions.ShouldBe(["tenant.suspended", "tenant.resumed", "tenant.archived"]);
    }

    [Fact]
    public async Task Suspending_revokes_the_customers_refresh_tokens()
    {
        using var anonymous = App.CreateClient();
        var session = await (await anonymous.PostJsonAsync("/api/v1/auth/login", Credentials(World.A.Technician.Email))).ShouldBeOkAsync<MonitorCloud.Application.Identity.AuthResultDto>();
        using var client = App.ClientFor(World.PlatformAdmin);
        await client.PostJsonAsync($"/api/v1/platform/tenants/{World.A.Id}/suspend", new { reason = "Unpaid invoice" });

        await DispatchOutboxAsync();

        var active = await App.InDbAsync(db => db.Set<RefreshToken>().CountAsync(t => t.UserId == World.A.Technician.Id && t.RevokedAt == null));
        active.ShouldBe(0);
        session.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Archived_customers_cannot_be_opened_as_a_workspace()
    {
        using var client = App.ClientFor(World.PlatformAdmin);
        await client.PostJsonAsync($"/api/v1/platform/tenants/{World.B.Id}/archive", new { reason = "Contract ended" });

        await (await client.PostJsonAsync($"/api/v1/platform/tenants/{World.B.Id}/workspace-sessions", new { reason = "Investigating a ticket" })).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
        using var workspace = App.ClientFor(World.PlatformAdmin, World.B.Id);
        await (await workspace.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Get_and_update_use_etags()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        using var get = await client.GetAsync(new Uri($"/api/v1/platform/tenants/{World.A.Id}", UriKind.Relative));
        var tenant = await get.ShouldBeOkAsync<TenantDto>();
        get.Headers.ETag!.Tag.ShouldBe($"\"{tenant.Version}\"");

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/tenants/{World.A.Id}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Alpha Renamed", country = "Egypt", city = "Giza", timeZone = "Africa/Cairo" }),
        };
        request.Headers.IfMatch.Add(get.Headers.ETag);
        var updated = await (await client.SendAsync(request)).ShouldBeOkAsync<TenantDto>();
        updated.Name.ShouldBe("Alpha Renamed");
        updated.Version.ShouldNotBe(tenant.Version);
    }

    [Fact]
    public async Task Unknown_customer_is_not_found()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        await (await client.GetAsync(new Uri($"/api/v1/platform/tenants/{Guid.CreateVersion7()}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Workspace_sessions_need_a_reason_and_are_audited()
    {
        using var client = App.ClientFor(World.PlatformSupport);
        var url = $"/api/v1/platform/tenants/{World.A.Id}/workspace-sessions";

        await (await client.PostJsonAsync(url, new { reason = "short" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        (await client.PostJsonAsync(url, new { reason = "Customer called about an alert" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        App.Clock.Advance(TimeSpan.FromSeconds(1));
        (await client.DeleteAsync(new Uri(url + "/current", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var audit = await App.InDbAsync(db => db.Set<AuditRecord>().Where(a => a.Action.StartsWith("workspace.")).OrderBy(a => a.At).ToListAsync());
        audit.Select(a => a.Action).ShouldBe(["workspace.opened", "workspace.closed"]);
        audit[0].Details.ShouldBe("Customer called about an alert");
        audit[0].ActorId.ShouldBe(World.PlatformSupport.Id);
    }

    [Fact]
    public async Task Platform_audit_lists_records_with_filters()
    {
        using var client = App.ClientFor(World.PlatformAdmin);
        await client.PostJsonAsync($"/api/v1/platform/tenants/{World.A.Id}/workspace-sessions", new { reason = "Customer called about an alert" });

        var page = await (await client.GetAsync(new Uri("/api/v1/platform/audit?action=workspace", UriKind.Relative)))
            .ShouldBeOkAsync<PagedResult<MonitorCloud.Application.Audit.AuditRecordDto>>();

        page.Items.ShouldHaveSingleItem().Action.ShouldBe("workspace.opened");
    }

    [Fact]
    public async Task Tenant_audit_shows_only_its_own_records()
    {
        using var adminA = App.ClientFor(World.A.Administrator);
        using var adminB = App.ClientFor(World.B.Administrator);
        await adminA.PostJsonAsync("/api/v1/locations", new { name = "Alpha East", code = "A-E", timeZone = "Africa/Cairo" });

        var a = await (await adminA.GetAsync(new Uri("/api/v1/audit", UriKind.Relative))).ShouldBeOkAsync<PagedResult<MonitorCloud.Application.Audit.AuditRecordDto>>();
        var b = await (await adminB.GetAsync(new Uri("/api/v1/audit", UriKind.Relative))).ShouldBeOkAsync<PagedResult<MonitorCloud.Application.Audit.AuditRecordDto>>();

        a.Items.ShouldContain(r => r.Action == "location.created");
        b.Items.ShouldNotContain(r => r.Action == "location.created");
    }
}
