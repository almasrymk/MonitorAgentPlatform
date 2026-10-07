using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Telemetry;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Telemetry;
using MonitorCloud.Infrastructure.Telemetry;
using MonitorCloud.SimulatedAgent;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.GatewayTests;

/// <summary>A host with a small ingestion channel so back-pressure is observable.</summary>
public sealed class TelemetryTestApp(SqlServerFixture sql) : TestApp(sql)
{
    public const int ChannelCapacity = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Telemetry:ChannelCapacity", ChannelCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>Gateway tests 4, 5, 8 and 12 of 09 section 5, plus the device screen queries over the ingested data.</summary>
[Collection(SqlCollection.Name)]
public sealed class TelemetryGatewayTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TelemetryTestApp _app = new(sql);
    private TestWorld _world = null!;
    private GrpcChannel _channel = null!;

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        _world = await TestWorld.CreateAsync(_app);
        _channel = GrpcChannel.ForAddress(_app.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = _app.Server.CreateHandler() });
    }

    public async Task DisposeAsync()
    {
        _app.Services.GetRequiredService<TelemetryWriter>().Resume();
        _channel.Dispose();
        await _app.DisposeAsync();
    }

    private TelemetryWriter Writer => _app.Services.GetRequiredService<TelemetryWriter>();

    private DateTimeOffset Now => _app.Clock.GetUtcNow();

    private async Task<SimulatedAgent.SimulatedAgent> ConnectedAsync(Device device)
    {
        var identity = new AgentIdentity(device.Id, TestWorld.DeviceSecret, device.Fingerprint, device.Hostname, string.Empty);
        var agent = new SimulatedAgent.SimulatedAgent(identity, _channel);
        var token = await new AgentCloudClient(_app.CreateClient()).TokenAsync(identity);
        (await agent.ConnectAsync(token)).ShouldNotBeNull();
        return agent;
    }

    private Task<int> MinutesAsync(Guid deviceId) => _app.InDbAsync(db => db.Set<MetricMinute>().CountAsync(m => m.DeviceId == deviceId));

    private static async Task Eventually(Func<Task<bool>> condition, string what, int seconds = 10)
    {
        for (var i = 0; i < seconds * 20; i++)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        throw new ShouldAssertException($"Timed out waiting for {what}.");
    }

    // ---------------------------------------------------------------- 4

    [Fact]
    public async Task A_metric_batch_is_stored_updates_the_state_and_is_acknowledged_after_commit_once()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var from = Now.AddMinutes(-5);

        var sequence = await agent.SendMetricsAsync(from, count: 5);

        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(5);
        var state = await _app.InDbAsync(db => db.Set<DeviceState>().AsNoTracking().SingleAsync(s => s.DeviceId == _world.A.Device1.Id));
        state.CpuPercent.ShouldBe((decimal)agent.Metrics.Minute(from.AddMinutes(4)).CpuAvg);
        state.LastEventSequence.ShouldBe((long)sequence);
        state.DiskPercent.ShouldNotBeNull();
        (await _app.InDbAsync(db => db.Set<DiskUsageHour>().CountAsync(d => d.DeviceId == _world.A.Device1.Id))).ShouldBe(1);

        // The same batch again (same sequence) is acknowledged and not stored twice.
        var resent = agent.Received.Count(m => m.Ack is not null);
        await agent.ResendAsync(new AgentMessage { Sequence = sequence, MessageId = "resend", MetricBatch = new MetricBatch { Minutes = { agent.Metrics.Minute(from) } } });
        await Eventually(() => Task.FromResult(agent.Received.Count(m => m.Ack is not null) > resent), "the duplicate acknowledgement");
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task A_batch_sent_while_the_database_is_down_is_acknowledged_only_after_it_recovers()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        Writer.SimulateDatabaseFailure = true;

        var sequence = await agent.SendMetricsAsync(Now.AddMinutes(-2), count: 2);
        await Task.Delay(1500);

        agent.Acknowledged.ShouldBeLessThan(sequence);
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(0);
        Writer.SimulateDatabaseFailure = false;
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement after recovery", 15);
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Rows_of_a_forged_device_never_reach_another_tenant()
    {
        // Device A1's stream can only write A1's rows: the device comes from the token.
        await using var agent = await ConnectedAsync(_world.A.Device1);

        var sequence = await agent.SendMetricsAsync(Now.AddMinutes(-1));

        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        (await _app.InDbAsync(db => db.Set<MetricMinute>().Select(m => new { m.DeviceId, m.TenantId }).Distinct().ToListAsync()))
            .ShouldHaveSingleItem().ShouldBe(new { DeviceId = _world.A.Device1.Id, TenantId = _world.A.Id });
    }

    // ---------------------------------------------------------------- 5

    [Fact]
    public async Task A_three_hour_backlog_is_accepted_in_order_and_minutes_beyond_retention_are_dropped()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var start = Now.AddHours(-3);

        ulong last = 0;
        for (var chunk = 0; chunk < 3; chunk++)
            last = await agent.SendMetricsAsync(start.AddMinutes(chunk * 60), count: 60, withDisks: chunk == 0);
        var old = await agent.SendMetricsAsync(Now.AddDays(-31), count: 10);

        await Eventually(() => Task.FromResult(agent.Acknowledged >= old), "all acknowledgements");
        last.ShouldBeLessThan(old);
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(180);
    }

    [Fact]
    public async Task The_rollup_aggregates_completed_hours_with_weighted_averages()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var hour = new DateTimeOffset(Now.Year, Now.Month, Now.Day, Now.Hour, 0, 0, TimeSpan.Zero).AddHours(-2);
        var sequence = await agent.SendMetricsAsync(hour, count: 60);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");

        var merged = await _app.Services.GetRequiredService<TelemetryJobs>().RollupAsync(CancellationToken.None);
        await _app.Services.GetRequiredService<TelemetryJobs>().RollupAsync(CancellationToken.None);

        merged.ShouldBe(1);
        var rows = await _app.InDbAsync(db => db.Set<MetricHour>().AsNoTracking().Where(h => h.DeviceId == _world.A.Device1.Id).ToListAsync());
        var row = rows.ShouldHaveSingleItem();
        row.Samples.ShouldBe((short)(60 * 12));
        var minutes = Enumerable.Range(0, 60).Select(i => agent.Metrics.Minute(hour.AddMinutes(i))).ToList();
        row.CpuAvg.ShouldBe((decimal)Math.Round(minutes.Average(m => m.CpuAvg), 2), 0.02m);
        row.CpuMax.ShouldBe((decimal)minutes.Max(m => m.CpuMax));
    }

    [Fact]
    public async Task Retention_removes_old_rows()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var sequence = await agent.SendMetricsAsync(Now.AddMinutes(-3), count: 3);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        _app.Clock.Advance(TimeSpan.FromDays(31));

        var deleted = await _app.Services.GetRequiredService<TelemetryJobs>().RetentionAsync(CancellationToken.None);

        deleted.ShouldBe(3);
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(0);
    }

    // ---------------------------------------------------------------- 8

    [Fact]
    public async Task Live_sessions_switch_the_agent_to_live_mode_and_samples_reach_the_device_group()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var token = _app.TokenFor(_world.A.Technician);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_app.Server.BaseAddress, "/hubs/live"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var samples = new ConcurrentQueue<JsonElement>();
        hub.On<JsonElement>("liveSample", samples.Enqueue);
        await hub.StartAsync();
        await hub.InvokeAsync("SubscribeDevice", _world.A.Device1.Id);
        using var client = _app.ClientFor(_world.A.Technician);

        (await client.PostAsync(new Uri($"/api/v1/devices/{_world.A.Device1.Id}/live-sessions", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(new Uri($"/api/v1/devices/{_world.A.Device1.Id}/live-sessions", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Eventually(() => Task.FromResult(agent.Received.Any(m => m.SetMode is not null)), "SetTelemetryMode");
        var mode = agent.Received.Single(m => m.SetMode is not null).SetMode;
        mode.Mode.ShouldBe(TelemetryMode.Live);
        mode.LiveIntervalSeconds.ShouldBe(2u);
        mode.TtlSeconds.ShouldBe(60u);
        await Eventually(() => Task.FromResult(samples.Any(s => s.GetProperty("deviceId").GetGuid() == _world.A.Device1.Id)), "a live sample on the hub", 15);

        // Renewed only every 30 s; samples are not stored.
        _app.Clock.Advance(TimeSpan.FromSeconds(31));
        using var later = _app.ClientFor(_world.A.Technician);
        (await later.PostAsync(new Uri($"/api/v1/devices/{_world.A.Device1.Id}/live-sessions", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Eventually(() => Task.FromResult(agent.Received.Count(m => m.SetMode is not null) == 2), "the renewal");
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(0);
        agent.LiveUntil.ShouldNotBeNull();
    }

    [Fact]
    public async Task Live_sessions_of_another_tenants_device_are_not_found()
    {
        using var client = _app.ClientFor(_world.B.Technician);

        var response = await client.PostAsync(new Uri($"/api/v1/devices/{_world.A.Device1.Id}/live-sessions", UriKind.Relative), null);

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
    }

    // ---------------------------------------------------------------- 12

    [Fact]
    public async Task With_the_writer_paused_the_gateway_stops_reading_and_memory_stays_bounded()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        Writer.Pause();

        var sends = new List<Task<ulong>>();
        for (var i = 0; i < 20; i++)
            sends.Add(agent.SendMetricsAsync(Now.AddMinutes(-30 + i), withDisks: false));
        await Task.Delay(1500);

        Writer.Pending.ShouldBeLessThanOrEqualTo(TelemetryTestApp.ChannelCapacity);
        agent.Acknowledged.ShouldBe(0ul);
        Writer.Resume();
        await Task.WhenAll(sends);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= 20), "all acknowledgements after resume", 20);
        (await MinutesAsync(_world.A.Device1.Id)).ShouldBe(20);
    }

    // ---------------------------------------------------------------- device screen data

    [Fact]
    public async Task Snapshot_inventory_metrics_and_disks_are_served_to_the_device_screen()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        var from = Now.AddMinutes(-10);
        var metrics = await agent.SendMetricsAsync(from, count: 10);
        var inventory = await agent.InventoryAsync(AgentProtocol.V1.InventoryKind.Hardware);
        await agent.SnapshotAsync();
        // Acknowledged holds the highest sequence: the inventory (acknowledged at once) can overtake the metrics.
        await Eventually(async () => agent.Acknowledged >= inventory && await MinutesAsync(_world.A.Device1.Id) == 10, "the writes");
        using var client = _app.ClientFor(_world.A.ReportViewer);
        var id = _world.A.Device1.Id;

        DeviceOverviewDto? overview = null;
        await Eventually(async () =>
        {
            overview = await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/overview", UriKind.Relative))).ShouldBeOkAsync<DeviceOverviewDto>();
            return overview.Snapshot is not null;
        }, "the snapshot");
        overview!.Snapshot!.Value.GetProperty("cpu").GetProperty("model").GetString().ShouldBe("Intel Xeon Silver 4314");

        var series = await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/metrics?from={Uri.EscapeDataString(from.AddMinutes(-1).ToString("O"))}&metrics=cpu,ram", UriKind.Relative)))
            .ShouldBeOkAsync<DeviceMetricsDto>();
        series.Resolution.ShouldBe("minute");
        series.Series.Select(s => s.Metric).ShouldBe(["cpu", "ram"]);
        series.Series[0].Points.Count.ShouldBe(10);

        var disks = await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/disks", UriKind.Relative))).ShouldBeOkAsync<List<DiskDto>>();
        disks.ShouldHaveSingleItem().Drive.ShouldBe("C:");

        var hardware = await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/inventory/hardware", UriKind.Relative))).ShouldBeOkAsync<InventoryDto>();
        hardware.Document.GetProperty("hostname").GetString().ShouldBe(_world.A.Device1.Hostname);
        await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/inventory/programs", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "INVENTORY_NOT_FOUND");
        await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/inventory/bogus", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await client.GetAsync(new Uri($"/api/v1/devices/{id}/metrics?metrics=bogus", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        // The same hash again is not a change.
        var count = agent.Acknowledged;
        var again = await agent.InventoryAsync(AgentProtocol.V1.InventoryKind.Hardware);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= again), "the second acknowledgement");
        again.ShouldBeGreaterThan(count);
    }

    [Fact]
    public async Task Long_ranges_use_hourly_data()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);
        using var client = _app.ClientFor(_world.A.ReportViewer);

        var series = await (await client.GetAsync(new Uri($"/api/v1/devices/{_world.A.Device1.Id}/metrics?from={Uri.EscapeDataString(Now.AddDays(-7).ToString("O"))}", UriKind.Relative)))
            .ShouldBeOkAsync<DeviceMetricsDto>();

        series.Resolution.ShouldBe("hour");
        series.Series.Select(s => s.Metric).ShouldBe(["cpu", "ram", "disk", "network"]);
    }
}
