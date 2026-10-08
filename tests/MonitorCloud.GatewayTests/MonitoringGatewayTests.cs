using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Infrastructure.Devices;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.Infrastructure.Monitoring;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SimulatedAgent;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.GatewayTests;

/// <summary>Gateway tests 6, 7 (offline alert) and 10 of 09 section 5, the storm test of M6 and the cloud alerts.</summary>
[Collection(SqlCollection.Name)]
public sealed class MonitoringGatewayTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestApp _app = new(sql);
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
        _channel.Dispose();
        await _app.DisposeAsync();
    }

    private DateTimeOffset Now => _app.Clock.GetUtcNow();

    private async Task<SimulatedAgent.SimulatedAgent> ConnectedAsync(AgentIdentity identity, TimeProvider? clock = null)
    {
        var agent = new SimulatedAgent.SimulatedAgent(identity, _channel) { Clock = clock ?? _app.Clock };
        var token = await new AgentCloudClient(_app.CreateClient()).TokenAsync(identity);
        (await agent.ConnectAsync(token)).ShouldNotBeNull();
        return agent;
    }

    private Task<SimulatedAgent.SimulatedAgent> ConnectedAsync(Device device, TimeProvider? clock = null) =>
        ConnectedAsync(new AgentIdentity(device.Id, TestWorld.DeviceSecret, device.Fingerprint, device.Hostname, string.Empty), clock);

    private async Task DispatchAsync()
    {
        var dispatcher = _app.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchBatchAsync(CancellationToken.None) > 0)
        {
        }
    }

    private Task RunJobsAsync() => _app.Services.GetRequiredService<MonitoringJobs>().RunOnceAsync(CancellationToken.None);

    private Task<List<Alert>> AlertsAsync(Guid deviceId) => _app.InDbAsync(db => db.Set<Alert>().AsNoTracking().Where(a => a.DeviceId == deviceId).ToListAsync());

    private Task<DeviceState> StateAsync(Guid deviceId) => _app.InDbAsync(db => db.Set<DeviceState>().AsNoTracking().SingleAsync(s => s.DeviceId == deviceId));

    private Task<int> NotificationsAsync(Guid tenantId) => _app.InDbAsync(db => db.Set<Notification>().CountAsync(n => n.TenantId == tenantId));

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

    private async Task<HubConnection> HubAsync(Domain.Identity.User user)
    {
        var token = _app.TokenFor(user);
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_app.Server.BaseAddress, "/hubs/live"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await hub.StartAsync();
        return hub;
    }

    // ---------------------------------------------------------------- 6

    [Fact]
    public async Task A_raised_issue_becomes_an_alert_changes_health_and_tiles_notifies_once_and_resolves_when_cleared()
    {
        var device = _world.A.Device1;
        await using var hub = await HubAsync(_world.A.Administrator);
        var raised = new ConcurrentQueue<JsonElement>();
        var resolved = new ConcurrentQueue<JsonElement>();
        var notified = new ConcurrentQueue<JsonElement>();
        hub.On<JsonElement>("alertRaised", raised.Enqueue);
        hub.On<JsonElement>("alertResolved", resolved.Enqueue);
        hub.On<JsonElement>("notificationCreated", notified.Enqueue);
        await hub.InvokeAsync("SubscribeTenant", (Guid?)null);
        await using var agent = await ConnectedAsync(device);

        var sequence = await agent.IssueAsync("cpu", AgentProtocol.V1.IssueAction.Raised, Severity.Warning, "Performance", "High CPU usage");
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        await DispatchAsync();

        var alert = (await AlertsAsync(device.Id)).Single(a => a.IssueKey == "cpu");
        alert.IsOpen.ShouldBeTrue();
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.LocationId.ShouldBe(_world.A.Location1.Id);
        var state = await StateAsync(device.Id);
        state.Health.ShouldBe(DeviceHealth.Warning);
        state.OpenWarning.ShouldBe(1);
        using var admin = _app.ClientFor(_world.A.Administrator);
        var summary = await (await admin.GetAsync(new Uri("/api/v1/devices/summary", UriKind.Relative))).ShouldBeOkAsync<DevicesSummaryDto>();
        summary.Warning.ShouldBe(1);
        var open = await (await admin.GetAsync(new Uri($"/api/v1/alerts?deviceId={device.Id}", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        open.Items.ShouldContain(a => a.Id == alert.Id && a.DeviceName == device.Name && a.CanResolve);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(1);
        await Eventually(() => Task.FromResult(raised.Any(e => e.GetProperty("alertId").GetGuid() == alert.Id)), "alertRaised");
        await Eventually(() => Task.FromResult(!notified.IsEmpty), "notificationCreated");

        // One e-mail for the recipient (Enterprise includes notifications.email), sent by the delivery job.
        await RunJobsAsync();
        _app.Mail.Sent.ShouldContain(m => m.To == _world.A.Recipient1.Email && m.Subject.Contains("High CPU usage", StringComparison.Ordinal));
        (await _app.InDbAsync(db => db.Set<NotificationDelivery>().SingleAsync(d => d.AlertId == alert.Id))).Status.ShouldBe(DeliveryStatus.Sent);

        // Cleared: resolved, health back to Healthy, gone from the open list.
        var cleared = await agent.IssueAsync("cpu", AgentProtocol.V1.IssueAction.Cleared);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= cleared), "the acknowledgement of the clear");
        await DispatchAsync();

        (await AlertsAsync(device.Id)).Single(a => a.Id == alert.Id).ResolvedBy.ShouldBe(ResolvedBy.Auto);
        (await StateAsync(device.Id)).Health.ShouldBe(DeviceHealth.Healthy);
        (await (await admin.GetAsync(new Uri($"/api/v1/alerts?deviceId={device.Id}", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>())
            .Items.ShouldNotContain(a => a.Id == alert.Id);
        await Eventually(() => Task.FromResult(resolved.Any(e => e.GetProperty("alertId").GetGuid() == alert.Id)), "alertResolved");
        (await NotificationsAsync(_world.A.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_storm_of_500_raises_creates_one_alert_and_one_notification()
    {
        var device = _world.A.Device1;
        await using var agent = await ConnectedAsync(device);

        ulong last = 0;
        for (var i = 0; i < 500; i++)
            last = await agent.IssueAsync("ram", AgentProtocol.V1.IssueAction.Raised, Severity.Critical, "Performance", "Memory exhausted");
        await Eventually(() => Task.FromResult(agent.Acknowledged >= last), "500 acknowledgements", 60);
        await DispatchAsync();

        var alert = (await AlertsAsync(device.Id)).Where(a => a.IssueKey == "ram").ShouldHaveSingleItem();
        alert.Occurrences.ShouldBe(500);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(1);
        (await StateAsync(device.Id)).Health.ShouldBe(DeviceHealth.Critical);
    }

    [Fact]
    public async Task A_severity_change_updates_the_alert_and_the_health()
    {
        var device = _world.A.Device1;
        await using var agent = await ConnectedAsync(device);

        await agent.IssueAsync("disk-C", AgentProtocol.V1.IssueAction.Raised, Severity.Warning, "Storage");
        var changed = await agent.IssueAsync("disk-C", AgentProtocol.V1.IssueAction.SeverityChanged, Severity.Critical, "Storage");
        await Eventually(() => Task.FromResult(agent.Acknowledged >= changed), "the acknowledgement");
        await DispatchAsync();

        var alert = (await AlertsAsync(device.Id)).Single(a => a.IssueKey == "disk-C");
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.Occurrences.ShouldBe(2);
        (await StateAsync(device.Id)).Health.ShouldBe(DeviceHealth.Critical);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_backlog_issue_older_than_15_minutes_keeps_its_time_and_does_not_notify()
    {
        var device = _world.A.Device1;
        await using var agent = await ConnectedAsync(device);
        var occurred = Now.AddMinutes(-40);

        var sequence = await agent.IssueAsync("point:web", AgentProtocol.V1.IssueAction.Raised, Severity.Critical, "Service", occurredAt: occurred);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        await DispatchAsync();

        var alert = (await AlertsAsync(device.Id)).Single(a => a.IssueKey == "point:web");
        alert.FirstSeenAt.ShouldBe(occurred);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(0);
        (await _app.InDbAsync(db => db.Set<AlertDailyStat>().SumAsync(s => s.Critical))).ShouldBe(1);
    }

    [Fact]
    public async Task Cloud_issue_keys_cannot_be_sent_by_an_agent_and_are_acknowledged_and_dropped()
    {
        await using var agent = await ConnectedAsync(_world.A.Device1);

        var sequence = await agent.IssueAsync(CloudIssues.DeviceOffline, AgentProtocol.V1.IssueAction.Raised, Severity.Critical);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");

        (await AlertsAsync(_world.A.Device1.Id)).ShouldNotContain(a => a.IssueKey == CloudIssues.DeviceOffline);
    }

    [Fact]
    public async Task Alerts_of_one_tenant_never_reach_another()
    {
        await using var agent = await ConnectedAsync(_world.B.Device1);

        var sequence = await agent.IssueAsync("cpu", AgentProtocol.V1.IssueAction.Raised, Severity.Critical);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the acknowledgement");
        await DispatchAsync();

        using var a = _app.ClientFor(_world.A.Administrator);
        var alerts = await (await a.GetAsync(new Uri("/api/v1/alerts?status=all", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        alerts.Items.ShouldAllBe(x => x.DeviceId != _world.B.Device1.Id);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(0);
        (await NotificationsAsync(_world.B.Id)).ShouldBe(1);
    }

    // ---------------------------------------------------------------- 7 (alerts)

    [Fact]
    public async Task An_offline_device_gets_the_offline_alert_after_two_minutes_and_reconnecting_resolves_it()
    {
        var device = _world.A.Device1;
        var agent = await ConnectedAsync(device);
        await agent.GoodbyeAsync(GoodbyeReason.ServiceStopping);
        await agent.DisposeAsync();
        await Eventually(async () => (await StateAsync(device.Id)).Connection == ConnectionState.Offline, "the device offline");
        await DispatchAsync();

        _app.Clock.Advance(TimeSpan.FromSeconds(90));
        await RunJobsAsync();
        (await AlertsAsync(device.Id)).ShouldNotContain(a => a.IssueKey == CloudIssues.DeviceOffline, "not before the 2-minute delay");

        _app.Clock.Advance(TimeSpan.FromSeconds(31));
        await RunJobsAsync();
        await DispatchAsync();
        var offline = (await AlertsAsync(device.Id)).Single(a => a.IssueKey == CloudIssues.DeviceOffline);
        offline.Severity.ShouldBe(AlertSeverity.Critical);
        offline.Source.ShouldBe(AlertSource.Cloud);
        (await NotificationsAsync(_world.A.Id)).ShouldBe(1);

        // A user cannot resolve it; reconnecting does.
        using var admin = _app.ClientFor(_world.A.Administrator);
        await (await admin.PostAsync(new Uri($"/api/v1/alerts/{offline.Id}/resolve", UriKind.Relative), null)).ShouldBeProblemAsync(System.Net.HttpStatusCode.Conflict, "ALERT_SELF_RESOLVING");
        await using var again = await ConnectedAsync(device);
        await DispatchAsync();
        (await AlertsAsync(device.Id)).Single(a => a.Id == offline.Id).IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task A_host_shutdown_raises_an_Info_offline_alert_and_a_quick_reconnect_raises_none()
    {
        var device = _world.A.Device1;
        var agent = await ConnectedAsync(device);
        await agent.GoodbyeAsync(GoodbyeReason.HostShuttingDown);
        await agent.DisposeAsync();
        await Eventually(async () => (await StateAsync(device.Id)).Connection == ConnectionState.Offline, "the device offline");
        await DispatchAsync();
        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        await RunJobsAsync();
        (await AlertsAsync(device.Id)).Single(a => a.IssueKey == CloudIssues.DeviceOffline).Severity.ShouldBe(AlertSeverity.Info);

        var other = _world.A.Device2;
        var second = await ConnectedAsync(other);
        await second.GoodbyeAsync(GoodbyeReason.Updating);
        await second.DisposeAsync();
        await Eventually(async () => (await StateAsync(other.Id)).Connection == ConnectionState.Offline, "the second device offline");
        await DispatchAsync();
        await using var back = await ConnectedAsync(other);
        await DispatchAsync();
        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        await RunJobsAsync();
        (await AlertsAsync(other.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_offline_delay_and_severity_follow_the_tenant_settings()
    {
        using var admin = _app.ClientFor(_world.A.Administrator);
        (await admin.PutAsJsonAsync(new Uri("/api/v1/settings/general", UriKind.Relative),
            new { timeZone = "Africa/Cairo", defaultLanguage = "en", offlineAlertSeverity = "Warning", offlineAlertDelayMinutes = 5 })).EnsureSuccessStatusCode();
        var device = _world.A.Device1;
        var agent = await ConnectedAsync(device);
        await agent.GoodbyeAsync();
        await agent.DisposeAsync();
        await Eventually(async () => (await StateAsync(device.Id)).Connection == ConnectionState.Offline, "the device offline");
        await DispatchAsync();

        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        await RunJobsAsync();
        (await AlertsAsync(device.Id)).ShouldNotContain(a => a.IssueKey == CloudIssues.DeviceOffline);
        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        await RunJobsAsync();
        (await AlertsAsync(device.Id)).Single(a => a.IssueKey == CloudIssues.DeviceOffline).Severity.ShouldBe(AlertSeverity.Warning);
    }

    [Fact]
    public async Task When_every_device_of_a_location_goes_offline_one_location_notification_is_sent()
    {
        // Device2 is counted in Location1 for this test: both devices of the location go offline within 60 s.
        var location = _world.A.Location1.Id;
        await _app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            foreach (var state in await db.Set<DeviceState>().Where(s => s.TenantId == _world.A.Id).ToListAsync())
                state.SetConnection(ConnectionState.Offline, Now, TimeSpan.FromDays(14));
            db.Set<PendingOfflineAlert>().Add(PendingOfflineAlert.Create(_world.A.Device1.Id, _world.A.Id, location, "Timeout", Now));
            db.Set<PendingOfflineAlert>().Add(PendingOfflineAlert.Create(_world.A.Device2.Id, _world.A.Id, location, "Timeout", Now.AddSeconds(30)));
            await db.SaveChangesAsync();
        });

        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        await RunJobsAsync();
        await DispatchAsync();

        (await _app.InDbAsync(db => db.Set<Alert>().CountAsync(a => a.TenantId == _world.A.Id && a.IssueKey == CloudIssues.DeviceOffline))).ShouldBe(2);
        var notification = (await _app.InDbAsync(db => db.Set<Notification>().Where(n => n.TenantId == _world.A.Id).ToListAsync())).ShouldHaveSingleItem();
        notification.LocationId.ShouldBe(location);
        notification.DeviceId.ShouldBeNull();
        notification.Title.ShouldContain("offline");
    }

    // ---------------------------------------------------------------- clock skew, monitor points

    [Fact]
    public async Task A_device_clock_off_by_more_than_5_minutes_raises_clock_skew_and_a_correct_clock_resolves_it()
    {
        var device = _world.A.Device1;
        var skewed = new TestClock(Now.AddMinutes(10));
        await using (var agent = await ConnectedAsync(device, skewed))
        {
            await agent.HeartbeatAsync();
            await Eventually(async () => (await AlertsAsync(device.Id)).Any(a => a.IssueKey == CloudIssues.ClockSkew && a.IsOpen), "the clock-skew alert");
        }

        await using var fixedAgent = await ConnectedAsync(device);
        await fixedAgent.HeartbeatAsync();
        await Eventually(async () => (await AlertsAsync(device.Id)).Single(a => a.IssueKey == CloudIssues.ClockSkew).IsOpen == false, "the clock-skew alert resolved");
    }

    [Fact]
    public async Task Monitor_point_reports_import_the_points_and_a_full_report_removes_missing_ones()
    {
        var device = _world.A.Device1;
        await using var agent = await ConnectedAsync(device);
        MonitorPointStatus Point(string key, AgentProtocol.V1.PointStatus status) => new()
        {
            Key = key, DisplayName = key.ToUpperInvariant(), Type = "Website", Target = $"https://{key}.example.test", Enabled = true, Status = status, Message = "ok",
            ResponseMs = 42.5, LastChecked = Timestamp.FromDateTimeOffset(Now), StatusSince = Timestamp.FromDateTimeOffset(Now.AddHours(-1)), IntervalSeconds = 60,
        };

        var first = await agent.MonitorPointsAsync([Point("shop", AgentProtocol.V1.PointStatus.PointHealthy), Point("api", AgentProtocol.V1.PointStatus.PointCritical)]);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= first), "the acknowledgement");
        using var admin = _app.ClientFor(_world.A.Administrator);
        var points = await (await admin.GetAsync(new Uri($"/api/v1/devices/{device.Id}/monitor-points", UriKind.Relative))).ShouldBeOkAsync<List<MonitorPointDto>>();
        points.Select(p => (p.Key, p.Status)).ShouldBe([("shop", "Healthy"), ("api", "Critical")], ignoreOrder: true);
        points.Single(p => p.Key == "api").ResponseMs.ShouldBe(42.5m);

        // The sampler writes one row per point and minute.
        (await _app.Services.GetRequiredService<MonitoringJobs>().SampleMonitorPointsAsync(CancellationToken.None)).ShouldBe(2);
        (await _app.Services.GetRequiredService<MonitoringJobs>().SampleMonitorPointsAsync(CancellationToken.None)).ShouldBe(0);

        var second = await agent.MonitorPointsAsync([Point("shop", AgentProtocol.V1.PointStatus.PointWarning)]);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= second), "the acknowledgement");
        points = await (await admin.GetAsync(new Uri($"/api/v1/devices/{device.Id}/monitor-points", UriKind.Relative))).ShouldBeOkAsync<List<MonitorPointDto>>();
        points.ShouldHaveSingleItem().Status.ShouldBe("Warning");
    }

    // ---------------------------------------------------------------- 10

    [Fact]
    public async Task The_refresh_job_pushes_LicenseUpdate_and_a_rejected_refresh_makes_the_device_unlicensed()
    {
        var store = _app.Services.GetRequiredService<FakeLicensingStore>();
        var key = store.Snapshot().Licenses.Single(l => l.CustomerId == _world.LicensingCustomerOf(_world.A)).ProductKey;
        var cloud = new AgentCloudClient(_app.CreateClient());
        var identity = await cloud.EnrollAsync(new EnrollRequest(key, "sim-licence-0001", "SIM-LIC-01", LocationCode: "LOC-ALPHA-TEST"));
        await using var agent = await ConnectedAsync(identity);
        await DispatchAsync();
        var before = agent.Received.Count(m => m.LicenseUpdate is not null);

        // Due refresh: renewed and pushed.
        _app.Clock.Advance(TimeSpan.FromHours(25));
        await DeviceMaintenanceJob.RunOnceAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), _app.Clock, CancellationToken.None);
        await DispatchAsync();
        await Eventually(() => Task.FromResult(agent.Received.Count(m => m.LicenseUpdate is not null) > before), "LicenseUpdate after the renewal");
        var renewed = agent.Received.Last(m => m.LicenseUpdate is not null).LicenseUpdate;
        renewed.State.ShouldBe(LicenseState.Licensed);
        renewed.Token.ShouldNotBeNullOrEmpty();

        // Rejected refresh: Unlicensed, pushed with the reason, licence alert without changing the health (grace period).
        store.Mutate(data => data.Licenses.Single(l => l.CustomerId == _world.LicensingCustomerOf(_world.A)).Devices.Remove("sim-licence-0001"));
        _app.Clock.Advance(TimeSpan.FromHours(25));
        await DeviceMaintenanceJob.RunOnceAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), _app.Clock, CancellationToken.None);
        await DispatchAsync();
        await Eventually(() => Task.FromResult(agent.Received.Any(m => m.LicenseUpdate?.State == LicenseState.Unlicensed)), "LicenseUpdate Unlicensed");
        agent.Received.Last(m => m.LicenseUpdate is not null).LicenseUpdate.ReasonCode.ShouldBe("LIC_DEVICE_NOT_ACTIVATED");
        var state = await StateAsync(identity.DeviceId);
        state.LicenseState.ShouldBe(LicenseStateValue.Unlicensed);
        state.Health.ShouldBe(DeviceHealth.Healthy, "an unlicensed device keeps its health during the grace period");
        (await AlertsAsync(identity.DeviceId)).Single(a => a.IssueKey == CloudIssues.License).Category.ShouldBe(AlertCategories.License);

        // After the grace period the health is Unknown and only Hello/Heartbeat/Goodbye are processed on a new session.
        _app.Clock.Advance(TimeSpan.FromDays(15));
        await DeviceMaintenanceJob.RunOnceAsync(_app.Services.GetRequiredService<IServiceScopeFactory>(), _app.Clock, CancellationToken.None);
        (await StateAsync(identity.DeviceId)).Health.ShouldBe(DeviceHealth.Unknown);
        await using var restricted = await ConnectedAsync(identity);
        var dropped = await restricted.IssueAsync("cpu", AgentProtocol.V1.IssueAction.Raised, Severity.Critical);
        await Eventually(() => Task.FromResult(restricted.Acknowledged >= dropped), "the acknowledgement of the dropped issue");
        (await AlertsAsync(identity.DeviceId)).ShouldNotContain(a => a.IssueKey == "cpu");
        (await _app.InDbAsync(db => db.Set<DeviceLicense>().SingleAsync(l => l.DeviceId == identity.DeviceId))).State.ShouldBe(DeviceLicenseState.Unlicensed);
    }
}
