using System.Net;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Configuration;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Device configuration, monitor point CRUD and tenant defaults (MC-801, MC-802).</summary>
[Collection(SqlCollection.Name)]
public sealed class ConfigurationTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private static object Document(double cpuCritical = 90, double cpuWarning = 70) => new
    {
        telemetry = new { sampleSeconds = 5 },
        thresholds = new
        {
            cpu = new { warningPercent = cpuWarning, criticalPercent = cpuCritical, forSeconds = 120, clearBelowPercent = 60 },
            ram = new { warningPercent = 80, criticalPercent = 95, forSeconds = 300, clearBelowPercent = 75 },
            disk = new { warningPercent = 85, criticalPercent = 92, forSeconds = 60 },
            tempC = new { critical = 85, forSeconds = 120 },
        },
        features = new { remoteActions = false },
    };

    private static object Point(string type = "Website", string target = "https://shop.alpha.test/health", string? key = "shop") =>
        new { key, displayName = "Shop", type, target, intervalSeconds = 60, alertLevel = "Problem", enabled = true, showInShortcut = true, settings = new { expectStatus = 200 } };

    [Fact]
    public async Task A_device_without_configuration_shows_the_defaults_and_the_first_save_creates_version_2()
    {
        using var client = App.ClientFor(World.A.ItManager);
        var device = World.A.Device1.Id;

        using var first = await client.GetAsync(new Uri($"/api/v1/devices/{device}/configuration", UriKind.Relative));
        var defaults = await first.ShouldBeOkAsync<DeviceConfigurationDto>();
        defaults.Version.ShouldBe(0);
        defaults.Document.Thresholds.Cpu.CriticalPercent.ShouldBe(95);

        var saved = await (await client.PutJsonAsync($"/api/v1/devices/{device}/configuration", Document())).ShouldBeOkAsync<DeviceConfigurationDto>();
        saved.Version.ShouldBe(2, "created at version 1 from the defaults, then updated");
        saved.Document.Thresholds.Cpu.CriticalPercent.ShouldBe(90);
        (await App.InDbAsync(db => db.Set<AuditRecord>().AnyAsync(a => a.Action == "device.configuration_updated"))).ShouldBeTrue();
        (await App.InDbAsync(db => db.Set<MonitorCloud.Infrastructure.Messaging.OutboxMessage>().CountAsync(m => m.Type.Contains("DeviceConfigurationChangedV1")))).ShouldBe(1);
    }

    [Fact]
    public async Task If_Match_with_an_old_version_is_a_conflict()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var device = World.A.Device1.Id;
        (await client.PutJsonAsync($"/api/v1/devices/{device}/configuration", Document())).EnsureSuccessStatusCode();
        using var read = await client.GetAsync(new Uri($"/api/v1/devices/{device}/configuration", UriKind.Relative));
        var etag = read.Headers.ETag!.Tag;
        etag.ShouldBe("\"2\"");

        using var stale = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/devices/{device}/configuration", UriKind.Relative)) { Content = System.Net.Http.Json.JsonContent.Create(Document(85)) };
        stale.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue("\"1\""));
        await (await client.SendAsync(stale)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");

        using var current = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/devices/{device}/configuration", UriKind.Relative)) { Content = System.Net.Http.Json.JsonContent.Create(Document(85)) };
        current.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag));
        (await (await client.SendAsync(current)).ShouldBeOkAsync<DeviceConfigurationDto>()).Version.ShouldBe(3);
    }

    [Fact]
    public async Task The_first_save_with_If_Match_0_is_accepted()
    {
        using var client = App.ClientFor(World.A.Administrator);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/devices/{World.A.Device2.Id}/configuration", UriKind.Relative)) { Content = System.Net.Http.Json.JsonContent.Create(Document()) };
        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue("\"0\""));

        (await (await client.SendAsync(request)).ShouldBeOkAsync<DeviceConfigurationDto>()).Version.ShouldBe(2);
    }

    [Fact]
    public async Task An_invalid_document_is_rejected_with_field_errors()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var problem = await (await client.PutJsonAsync($"/api/v1/devices/{World.A.Device1.Id}/configuration", Document(cpuCritical: 60, cpuWarning: 70)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        problem.GetProperty("errors").TryGetProperty("thresholds.cpu.criticalPercent", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_technician_cannot_change_the_configuration_but_can_read_it()
    {
        using var client = App.ClientFor(World.A.Technician);

        (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device1.Id}/configuration", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.PutJsonAsync($"/api/v1/devices/{World.A.Device1.Id}/configuration", Document())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Monitor_points_are_created_updated_and_deleted_and_each_change_bumps_the_version()
    {
        using var client = App.ClientFor(World.A.Technician);
        var device = World.A.Device1.Id;

        var created = await (await client.PostJsonAsync($"/api/v1/devices/{device}/monitor-points", Point())).ShouldBeOkAsync<MonitorPointDto>(HttpStatusCode.Created);
        created.Origin.ShouldBe("Cloud");
        created.Settings!.Value.GetProperty("expectStatus").GetInt32().ShouldBe(200);
        await (await client.PostJsonAsync($"/api/v1/devices/{device}/monitor-points", Point())).ShouldBeProblemAsync(HttpStatusCode.Conflict, "MONITOR_POINT_KEY_TAKEN");
        await (await client.PostJsonAsync($"/api/v1/devices/{device}/monitor-points", Point(target: "not a url", key: "bad"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var updated = await (await client.PutJsonAsync($"/api/v1/devices/{device}/monitor-points/{created.Id}", Point(type: "Ping", target: "198.51.100.20"))).ShouldBeOkAsync<MonitorPointDto>();
        updated.Type.ShouldBe("Ping");
        (await client.DeleteAsync(new Uri($"/api/v1/devices/{device}/monitor-points/{World.A.Point1.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.DeleteAsync(new Uri($"/api/v1/devices/{device}/monitor-points/{World.A.Point1.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "MONITOR_POINT_NOT_FOUND");

        var configuration = await App.InDbAsync(db => db.Set<DeviceConfiguration>().SingleAsync(c => c.DeviceId == device));
        configuration.Version.ShouldBe(4, "created at 1, then create, update and delete");
        var points = await (await client.GetAsync(new Uri($"/api/v1/devices/{device}/monitor-points", UriKind.Relative))).ShouldBeOkAsync<List<MonitorPointDto>>();
        points.ShouldHaveSingleItem().Key.ShouldBe("shop");
    }

    [Fact]
    public async Task A_Starter_tenant_cannot_edit_monitor_points()
    {
        using var client = App.ClientFor(World.B.Administrator);

        var problem = await (await client.PostJsonAsync($"/api/v1/devices/{World.B.Device1.Id}/monitor-points", Point())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");

        problem.GetProperty("feature").GetString().ShouldBe("monitorpoints");
        (await client.GetAsync(new Uri($"/api/v1/devices/{World.B.Device1.Id}/monitor-points", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tenant_defaults_are_used_for_the_configuration_of_new_devices()
    {
        using var client = App.ClientFor(World.A.Administrator);

        (await (await client.GetAsync(new Uri("/api/v1/settings/monitoring", UriKind.Relative))).ShouldBeOkAsync<ConfigDocument>()).Thresholds.Disk.CriticalPercent.ShouldBe(92);
        (await client.PutJsonAsync("/api/v1/settings/monitoring", Document(cpuCritical: 88))).EnsureSuccessStatusCode();
        await (await client.PutJsonAsync("/api/v1/settings/monitoring", Document(cpuCritical: 101))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        // Device2 has no configuration yet: the first monitor point creates it from the new defaults.
        (await client.PostJsonAsync($"/api/v1/devices/{World.A.Device2.Id}/monitor-points", Point(key: "d2"))).EnsureSuccessStatusCode();
        var configuration = await (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device2.Id}/configuration", UriKind.Relative))).ShouldBeOkAsync<DeviceConfigurationDto>();
        configuration.Document.Thresholds.Cpu.CriticalPercent.ShouldBe(88);
        configuration.Version.ShouldBe(2);
    }
}

