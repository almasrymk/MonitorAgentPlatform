using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Devices.Enroll;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Infrastructure.Devices;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class DevicesTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private FakeLicensingStore Licensing => App.Services.GetRequiredService<FakeLicensingStore>();

    /// <summary>Dispatches until the outbox is empty (handlers raise follow-up events).</summary>
    private async Task DispatchOutboxAsync()
    {
        var dispatcher = App.Services.GetRequiredService<OutboxDispatcher>();
        for (var i = 0; i < 10 && await dispatcher.DispatchBatchAsync(CancellationToken.None) > 0; i++)
        {
        }
    }

    private FakeLicense LicenseOf(TestTenant tenant) => Licensing.Snapshot().Licenses.Single(l => l.CustomerId == World.LicensingCustomerOf(tenant));

    private async Task<EnrollmentResult> EnrollAsync(string fingerprint, string hostname, string? locationCode = null, string os = "Windows")
    {
        using var client = App.CreateClient();
        return await (await client.PostJsonAsync("/api/agent/v1/enroll", new
        {
            productKey = LicenseOf(World.A).ProductKey, fingerprint, hostname, osFamily = os, osName = $"{os} test", protocolVersion = 1, locationCode,
        })).ShouldBeOkAsync<EnrollmentResult>();
    }

    private async Task<PagedResult<DeviceListItemDto>> ListAsync(HttpClient client, string query = "") =>
        await (await client.GetAsync(new Uri("/api/v1/devices" + query, UriKind.Relative))).ShouldBeOkAsync<PagedResult<DeviceListItemDto>>();

    /// <summary>Sets a device's state directly, as the gateway will from M4.</summary>
    private Task SetStateAsync(Guid deviceId, ConnectionState connection, int critical = 0, int warning = 0, decimal? cpu = null) =>
        App.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MonitorCloud.Infrastructure.Persistence.AppDbContext>();
            var state = await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == deviceId);
            var now = App.Clock.GetUtcNow();
            state.SetConnection(connection, now, TimeSpan.FromDays(14));
            state.SetOpenAlerts(critical, warning, now, TimeSpan.FromDays(14));
            state.SetMetrics(cpu, null, null, null, now);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task List_is_sorted_by_severity_then_name_and_filters_combine()
    {
        var critical = await EnrollAsync("ma-list-critical-01", "ZULU-CRIT");
        var warning = await EnrollAsync("ma-list-warning-01", "YANKEE-WARN", os: "Linux");
        var offline = await EnrollAsync("ma-list-offline-01", "ALPHA-OFF");
        await SetStateAsync(critical.DeviceId, ConnectionState.Online, critical: 1, cpu: 95);
        await SetStateAsync(warning.DeviceId, ConnectionState.Online, warning: 2, cpu: 50);
        using var client = App.ClientFor(World.A.ReportViewer);

        var all = await ListAsync(client);

        all.Total.ShouldBe(5);
        all.Items.Select(d => d.Name).ShouldBe(["ZULU-CRIT", "YANKEE-WARN", "ALPHA-OFF", "ALPHA-PC-01", "ALPHA-PC-02"]);
        all.Items[0].OpenAlerts.ShouldBe(1);
        all.Items[0].OpenAlertSeverity.ShouldBe("Critical");
        all.Items[3].LocationName.ShouldBe(World.A.Location1.Name);
        (await ListAsync(client, "?status=offline")).Items.Select(d => d.Name).ShouldBe(["ALPHA-OFF"]);
        (await ListAsync(client, "?status=critical")).Items.Single().Id.ShouldBe(critical.DeviceId);
        (await ListAsync(client, "?os=linux")).Items.Single().Id.ShouldBe(warning.DeviceId);
        (await ListAsync(client, "?status=online&os=windows")).Total.ShouldBe(3);
        (await ListAsync(client, "?search=pc-0")).Total.ShouldBe(2);
        (await ListAsync(client, $"?locationId={World.A.Location2.Id}")).Items.Single().Id.ShouldBe(World.A.Device2.Id);
        (await ListAsync(client, "?license=unlicensed")).Total.ShouldBe(0);
        (await ListAsync(client, "?sort=name")).Items[0].Name.ShouldBe("ALPHA-OFF");
        (await ListAsync(client, "?sort=-name")).Items[0].Name.ShouldBe("ZULU-CRIT");
        (await ListAsync(client, "?sort=cpu")).Items[0].Name.ShouldBe("ZULU-CRIT");
        (await ListAsync(client, "?status=bogus")).Total.ShouldBe(0);
        offline.LocationName.ShouldBe("Unassigned");
    }

    [Fact]
    public async Task Summary_counts_the_devices()
    {
        var extra = await EnrollAsync("ma-summary-0001", "SUMMARY-01");
        await SetStateAsync(extra.DeviceId, ConnectionState.Online, warning: 1);
        using var client = App.ClientFor(World.A.ReportViewer);

        var summary = await (await client.GetAsync(new Uri("/api/v1/devices/summary", UriKind.Relative))).ShouldBeOkAsync<DevicesSummaryDto>();
        var location1 = await (await client.GetAsync(new Uri($"/api/v1/devices/summary?locationId={World.A.Location1.Id}", UriKind.Relative))).ShouldBeOkAsync<DevicesSummaryDto>();

        summary.ShouldBe(new DevicesSummaryDto(3, 3, 0, 3, 0, 2, 1, 0, 3));
        location1.Total.ShouldBe(1);
    }

    [Fact]
    public async Task Details_show_the_device_with_an_etag()
    {
        using var client = App.ClientFor(World.A.ReportViewer);

        var response = await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device1.Id}", UriKind.Relative));
        var device = await response.ShouldBeOkAsync<DeviceDto>();

        response.Headers.ETag.ShouldNotBeNull();
        device.CustomerName.ShouldBe(World.A.Tenant.Name);
        device.LocationName.ShouldBe(World.A.Location1.Name);
        device.Connection.ShouldBe("Online");
        device.Health.ShouldBe("Healthy");
        device.Fingerprint.ShouldBe(World.A.Device1.Fingerprint);
    }

    [Fact]
    public async Task Rename_and_move_update_the_device_and_its_state_and_check_the_version()
    {
        using var client = App.ClientFor(World.A.ItManager);
        var before = await (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device1.Id}", UriKind.Relative))).ShouldBeOkAsync<DeviceDto>();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{World.A.Device1.Id}")
        {
            Content = JsonContent(new { name = "Reception PC", locationId = World.A.Location2.Id }),
        };
        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{before.Version}\""));
        var updated = await (await client.SendAsync(request)).ShouldBeOkAsync<DeviceListItemDto>();

        updated.Name.ShouldBe("Reception PC");
        updated.LocationId.ShouldBe(World.A.Location2.Id);
        var stateLocation = await App.InDbAsync(db => db.Set<DeviceState>().Where(s => s.DeviceId == World.A.Device1.Id).Select(s => s.LocationId).SingleAsync());
        stateLocation.ShouldBe(World.A.Location2.Id);

        using var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{World.A.Device1.Id}") { Content = JsonContent(new { name = "Again", locationId = World.A.Location1.Id }) };
        stale.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{before.Version}\""));
        await (await client.SendAsync(stale)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Moving_to_another_tenants_location_is_not_found()
    {
        using var client = App.ClientFor(World.A.ItManager);

        var response = await client.PutJsonAsync($"/api/v1/devices/{World.A.Device1.Id}", new { name = "PC", locationId = World.B.Location1.Id });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
    }

    [Fact]
    public async Task Retire_revokes_the_credential_releases_the_seat_and_hides_the_device()
    {
        var enrolled = await EnrollAsync("ma-retire-me-0001", "RETIRE-ME");
        using var client = App.ClientFor(World.A.ItManager);

        (await client.PostAsync(new Uri($"/api/v1/devices/{enrolled.DeviceId}/retire", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DispatchOutboxAsync();

        LicenseOf(World.A).Devices.ShouldNotContain("ma-retire-me-0001");
        var seat = await App.InDbAsync(db => db.Set<DeviceLicense>().SingleAsync(l => l.DeviceId == enrolled.DeviceId));
        seat.State.ShouldBe(DeviceLicenseState.Unlicensed);
        seat.ReasonCode.ShouldBe("DEVICE_RETIRED");
        (await ListAsync(client)).Items.ShouldNotContain(d => d.Id == enrolled.DeviceId);
        await (await client.GetAsync(new Uri($"/api/v1/devices/{enrolled.DeviceId}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
        await (await client.PostAsync(new Uri($"/api/v1/devices/{enrolled.DeviceId}/retire", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
        (await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Audit.AuditRecord>().AnyAsync(a => a.Action == "device.retired" && a.EntityId == enrolled.DeviceId.ToString()))).ShouldBeTrue();
    }

    [Fact]
    public async Task Unlicense_releases_the_seat_but_keeps_the_device_and_the_grace_period_applies()
    {
        var enrolled = await EnrollAsync("ma-unlicense-0001", "UNLICENSE-ME");
        await SetStateAsync(enrolled.DeviceId, ConnectionState.Online);
        using var client = App.ClientFor(World.A.ItManager);

        (await client.PostAsync(new Uri($"/api/v1/devices/{enrolled.DeviceId}/unlicense", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DispatchOutboxAsync();

        LicenseOf(World.A).Devices.ShouldNotContain("ma-unlicense-0001");
        var item = (await ListAsync(client, "?license=unlicensed")).Items.Single();
        item.Id.ShouldBe(enrolled.DeviceId);
        item.Health.ShouldBe("Healthy");

        // D19: after 14 days the unlicensed device's health is Unknown.
        App.Clock.Advance(TimeSpan.FromDays(15));
        await App.InSystemScopeAsync(sp => sp.GetRequiredService<DeviceGraceService>().RunAsync(CancellationToken.None));
        using var later = App.ClientFor(World.A.ItManager);
        (await ListAsync(later, "?license=unlicensed")).Items.Single().Health.ShouldBe("Unknown");

        // A second unlicense is a no-op.
        (await later.PostAsync(new Uri($"/api/v1/devices/{enrolled.DeviceId}/unlicense", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_refresh_job_renews_due_licences_and_unlicenses_rejected_ones()
    {
        var kept = await EnrollAsync("ma-refresh-keep-01", "REFRESH-KEEP");
        var lost = await EnrollAsync("ma-refresh-lost-01", "REFRESH-LOST");
        Licensing.Mutate(data => data.Licenses.Single(l => l.CustomerId == World.LicensingCustomerOf(World.A)).Devices.Remove("ma-refresh-lost-01"));
        App.Clock.Advance(TimeSpan.FromHours(25));

        await DeviceMaintenanceJob.RunOnceAsync(App.Services.GetRequiredService<IServiceScopeFactory>(), App.Clock, CancellationToken.None);
        await DispatchOutboxAsync();

        var seats = await App.InDbAsync(db => db.Set<DeviceLicense>().Where(l => l.DeviceId == kept.DeviceId || l.DeviceId == lost.DeviceId).ToListAsync());
        var keptSeat = seats.Single(s => s.DeviceId == kept.DeviceId);
        keptSeat.State.ShouldBe(DeviceLicenseState.Licensed);
        keptSeat.CheckAfter.ShouldBeGreaterThan(App.Clock.GetUtcNow());
        var lostSeat = seats.Single(s => s.DeviceId == lost.DeviceId);
        lostSeat.State.ShouldBe(DeviceLicenseState.Unlicensed);
        lostSeat.ReasonCode.ShouldBe("LIC_DEVICE_NOT_ACTIVATED");
        var state = await App.InDbAsync(db => db.Set<DeviceState>().SingleAsync(s => s.DeviceId == lost.DeviceId));
        state.LicenseState.ShouldBe(LicenseStateValue.Unlicensed);
    }

    [Fact]
    public async Task The_refresh_job_keeps_the_licence_when_licensing_is_unavailable()
    {
        var device = await EnrollAsync("ma-refresh-down-01", "REFRESH-DOWN");
        App.Clock.Advance(TimeSpan.FromHours(25));
        Licensing.Unavailable = true;

        await DeviceMaintenanceJob.RunOnceAsync(App.Services.GetRequiredService<IServiceScopeFactory>(), App.Clock, CancellationToken.None);

        Licensing.Unavailable = false;
        var seat = await App.InDbAsync(db => db.Set<DeviceLicense>().SingleAsync(l => l.DeviceId == device.DeviceId));
        seat.State.ShouldBe(DeviceLicenseState.Licensed);
        seat.CheckAfter.ShouldBe(App.Clock.GetUtcNow().AddHours(1));
    }

    [Fact]
    public async Task Old_enrollment_attempts_are_removed_after_90_days()
    {
        await EnrollAsync("ma-attempt-old-001", "ATTEMPT-OLD");
        App.Clock.Advance(TimeSpan.FromDays(91));

        await DeviceMaintenanceJob.RunOnceAsync(App.Services.GetRequiredService<IServiceScopeFactory>(), App.Clock, CancellationToken.None);

        (await App.InDbAsync(db => db.Set<EnrollmentAttempt>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Location_scoped_users_see_only_the_devices_of_their_locations()
    {
        using var client = App.ClientFor(World.A.RestrictedManager);

        var page = await ListAsync(client);

        page.Items.Select(d => d.Id).ShouldBe([World.A.Device1.Id]);
        await (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device2.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
    }

    [Fact]
    public async Task Location_cards_count_their_devices_and_a_location_with_devices_cannot_be_deleted()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var cards = await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeOkAsync<PagedResult<LocationCardDto>>();

        var north = cards.Items.Single(c => c.Id == World.A.Location1.Id);
        north.Devices.ShouldBe(1);
        north.Online.ShouldBe(1);
        north.HealthScore.ShouldBe(100m);
        await (await client.DeleteAsync(new Uri($"/api/v1/locations/{World.A.Location1.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "LOCATION_NOT_EMPTY");
    }

    private static StringContent JsonContent(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body, HttpAssertions.Json), System.Text.Encoding.UTF8, "application/json");
}

[Collection(SqlCollection.Name)]
public sealed class EnrollmentCodeTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    [Fact]
    public async Task A_code_is_shown_once_with_install_commands_and_listed_by_prefix()
    {
        using var client = App.ClientFor(World.A.ItManager);
        var url = $"/api/v1/locations/{World.A.Location2.Id}/enrollment-codes";

        var created = await (await client.PostJsonAsync(url, new { expiresInHours = 48, maxUses = 10 })).ShouldBeOkAsync<EnrollmentCodeCreatedDto>(HttpStatusCode.Created);

        created.Code.ShouldMatch("^LOC-[A-Z2-9]{6}-[A-Z2-9]{6}$");
        created.ExpiresAt.ShouldBe(App.Clock.GetUtcNow().AddHours(48));
        created.InstallCommands.Windows.ShouldContain(created.Code);
        created.InstallCommands.Linux.ShouldContain(created.Code);
        created.InstallCommands.MacOs.ShouldContain(created.Code);
        created.InstallCommands.Windows.ShouldContain("PRODUCTKEY");

        var list = await (await client.GetAsync(new Uri(url, UriKind.Relative))).ShouldBeOkAsync<List<EnrollmentCodeDto>>();
        var row = list.ShouldHaveSingleItem();
        row.CodePrefix.ShouldBe(EnrollmentCodes.Prefix(created.Code));
        row.Usable.ShouldBeTrue();
        (await client.GetStringAsync(new Uri(url, UriKind.Relative))).ShouldNotContain(created.Code);
        var stored = await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Tenancy.LocationEnrollmentCode>().SingleAsync(c => c.Id == created.Id));
        stored.CodeHash.ShouldNotContain(created.Code);
    }

    [Fact]
    public async Task A_revoked_code_is_listed_as_revoked_and_no_longer_enrolls()
    {
        using var client = App.ClientFor(World.A.ItManager);
        var url = $"/api/v1/locations/{World.A.Location1.Id}/enrollment-codes";

        (await client.DeleteAsync(new Uri($"{url}/{World.A.EnrollmentCodeId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var row = (await (await client.GetAsync(new Uri(url, UriKind.Relative))).ShouldBeOkAsync<List<EnrollmentCodeDto>>()).Single();
        row.Revoked.ShouldBeTrue();
        row.Usable.ShouldBeFalse();
        var key = App.Services.GetRequiredService<FakeLicensingStore>().Snapshot().Licenses.Single(l => l.CustomerId == World.LicensingCustomerOf(World.A)).ProductKey;
        using var agent = App.CreateClient();
        await (await agent.PostJsonAsync("/api/agent/v1/enroll", new { productKey = key, fingerprint = "ma-revoked-code-01", hostname = "HOST", protocolVersion = 1, locationCode = "LOC-ALPHA-TEST" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "ENROLL_INVALID_LOCATION_CODE");
        await (await client.DeleteAsync(new Uri($"{url}/{Guid.CreateVersion7()}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "ENROLLMENT_CODE_NOT_FOUND");
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(721, null)]
    [InlineData(24, 0)]
    public async Task Invalid_lifetimes_and_max_uses_are_rejected(int hours, int? maxUses)
    {
        using var client = App.ClientFor(World.A.ItManager);

        var response = await client.PostJsonAsync($"/api/v1/locations/{World.A.Location1.Id}/enrollment-codes", new { expiresInHours = hours, maxUses });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Creating_a_code_is_audited_without_the_code()
    {
        using var client = App.ClientFor(World.A.ItManager);

        var created = await (await client.PostJsonAsync($"/api/v1/locations/{World.A.Location1.Id}/enrollment-codes", new { expiresInHours = 1 })).ShouldBeOkAsync<EnrollmentCodeCreatedDto>(HttpStatusCode.Created);

        var details = await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Audit.AuditRecord>().Where(a => a.Action == "enrollment_code.created").Select(a => a.Details).SingleAsync());
        details.ShouldNotBeNull().ShouldNotContain(created.Code);
    }
}
