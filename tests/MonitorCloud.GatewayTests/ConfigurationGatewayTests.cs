using System.Net.Http.Json;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Configuration;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.SimulatedAgent;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.GatewayTests;

/// <summary>Gateway test 9 of 09 section 5 and the M8 acceptance: configuration changes reach the agent and change its alerting.</summary>
[Collection(SqlCollection.Name)]
public sealed class ConfigurationGatewayTests(SqlServerFixture sql) : IAsyncLifetime
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

    private Device Device => _world.A.Device1;

    private async Task<SimulatedAgent.SimulatedAgent> ConnectedAsync(int appliedVersion = 0, string? rejectWith = null)
    {
        var identity = new AgentIdentity(Device.Id, TestWorld.DeviceSecret, Device.Fingerprint, Device.Hostname, string.Empty);
        var agent = new SimulatedAgent.SimulatedAgent(identity, _channel) { Clock = _app.Clock, ConfigVersion = appliedVersion, RejectConfigWith = rejectWith };
        var token = await new AgentCloudClient(_app.CreateClient()).TokenAsync(identity);
        (await agent.ConnectAsync(token)).ShouldNotBeNull();
        return agent;
    }

    private async Task DispatchAsync()
    {
        var dispatcher = _app.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchBatchAsync(CancellationToken.None) > 0)
        {
        }
    }

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

    private static object Document(double critical, double warning) => new
    {
        telemetry = new { sampleSeconds = 5 },
        thresholds = new
        {
            cpu = new { warningPercent = warning, criticalPercent = critical, forSeconds = 0, clearBelowPercent = warning - 5 },
            ram = new { warningPercent = 80, criticalPercent = 95, forSeconds = 300 },
            disk = new { warningPercent = 85, criticalPercent = 92, forSeconds = 60 },
            tempC = new { critical = 85, forSeconds = 120 },
        },
        features = new { remoteActions = false },
    };

    private async Task<DeviceConfigurationDto> PutAsync(double critical, double warning)
    {
        using var admin = _app.ClientFor(_world.A.Administrator);
        using var response = await admin.PutAsJsonAsync(new Uri($"/api/v1/devices/{Device.Id}/configuration", UriKind.Relative), Document(critical, warning));
        var saved = await response.ShouldBeOkAsync<DeviceConfigurationDto>();
        await DispatchAsync();
        return saved;
    }

    private Task<DeviceConfigurationAck?> AckAsync() => _app.InDbAsync(db => db.Set<DeviceConfigurationAck>().AsNoTracking().SingleOrDefaultAsync(a => a.DeviceId == Device.Id));

    [Fact]
    public async Task An_update_reaches_the_agent_its_answer_is_recorded_and_the_applied_version_is_visible()
    {
        await using var agent = await ConnectedAsync();

        var saved = await PutAsync(90, 70);

        await Eventually(() => Task.FromResult(agent.ConfigVersion == saved.Version), "ConfigUpdate on the stream");
        ((double?)agent.Config!["thresholds"]!["cpu"]!["criticalPercent"]).ShouldBe(90);
        agent.Config["monitorPoints"]!.AsArray().ShouldContain(p => (string?)p!["key"] == "web");
        await Eventually(async () => (await AckAsync())?.AppliedVersion == saved.Version, "ConfigApplied recorded");
        await DispatchAsync();
        (await _app.InDbAsync(db => db.Set<DeviceState>().SingleAsync(s => s.DeviceId == Device.Id))).AppliedConfigVersion.ShouldBe(saved.Version);
        using var admin = _app.ClientFor(_world.A.Administrator);
        var shown = await (await admin.GetAsync(new Uri($"/api/v1/devices/{Device.Id}/configuration", UriKind.Relative))).ShouldBeOkAsync<DeviceConfigurationDto>();
        shown.AppliedVersion.ShouldBe(saved.Version);
        shown.Error.ShouldBeNull();
    }

    [Fact]
    public async Task An_offline_device_receives_the_new_version_on_its_next_Welcome()
    {
        var first = await PutAsync(90, 70);

        // Connects as an agent that applied an older version: ConfigUpdate follows Welcome.
        await using var agent = await ConnectedAsync(appliedVersion: first.Version - 1);

        await Eventually(() => Task.FromResult(agent.ConfigVersion == first.Version), "ConfigUpdate after Welcome");
        agent.Received.Count(m => m.ConfigUpdate is not null).ShouldBe(1);
    }

    [Fact]
    public async Task An_agent_that_rejects_a_document_keeps_its_version_and_the_error_is_reported()
    {
        await using var agent = await ConnectedAsync(rejectWith: "secretRef db-main is not stored on this device");

        var saved = await PutAsync(90, 70);

        await Eventually(async () => (await AckAsync())?.RejectedVersion == saved.Version, "the rejection");
        agent.ConfigVersion.ShouldBe(0);
        using var admin = _app.ClientFor(_world.A.Administrator);
        var shown = await (await admin.GetAsync(new Uri($"/api/v1/devices/{Device.Id}/configuration", UriKind.Relative))).ShouldBeOkAsync<DeviceConfigurationDto>();
        shown.RejectedVersion.ShouldBe(saved.Version);
        shown.Error.ShouldBe("secretRef db-main is not stored on this device");
        shown.AppliedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task Lowering_the_CPU_threshold_in_the_portal_makes_the_agent_raise_the_alert()
    {
        await using var agent = await ConnectedAsync();
        var high = await PutAsync(90, 70);
        await Eventually(() => Task.FromResult(agent.ConfigVersion == high.Version), "the first configuration");

        (await agent.EvaluateCpuAsync(50)).ShouldBe(0ul, "50 % is below 70 / 90");

        var low = await PutAsync(40, 30);
        await Eventually(() => Task.FromResult(agent.ConfigVersion == low.Version), "the lower thresholds");
        var sequence = await agent.EvaluateCpuAsync(50);
        sequence.ShouldBeGreaterThan(0ul);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= sequence), "the issue acknowledgement");
        await DispatchAsync();

        var alert = await _app.InDbAsync(db => db.Set<Alert>().SingleAsync(a => a.DeviceId == Device.Id && a.IssueKey == "cpu"));
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        (await agent.EvaluateCpuAsync(10)).ShouldBeGreaterThan(0ul, "below the clear level the agent clears it");
    }

    [Fact]
    public async Task A_monitor_point_edit_in_the_portal_is_pushed_and_later_reports_do_not_overwrite_it()
    {
        await using var agent = await ConnectedAsync();
        using var admin = _app.ClientFor(_world.A.Technician);
        (await admin.PutAsJsonAsync(new Uri($"/api/v1/devices/{Device.Id}/monitor-points/{_world.A.Point1.Id}", UriKind.Relative),
            new { displayName = "Shop (cloud)", type = "Website", target = "https://shop.alpha.test/health", intervalSeconds = 30, alertLevel = "Warning", enabled = true, showInShortcut = true }))
            .EnsureSuccessStatusCode();
        await DispatchAsync();
        await Eventually(() => Task.FromResult(agent.Config?["monitorPoints"]?[0]?["displayName"]?.GetValue<string>() == "Shop (cloud)"), "the pushed point");

        // The agent's next full report keeps its old name: the cloud definition wins, only the status is taken.
        var report = await agent.MonitorPointsAsync([new MonitorPointStatus { Key = "web", DisplayName = "Web shop", Type = "Website", Target = "https://old.alpha.test", Enabled = true, Status = AgentProtocol.V1.PointStatus.PointCritical }]);
        await Eventually(() => Task.FromResult(agent.Acknowledged >= report), "the report acknowledgement");
        var point = await _app.InDbAsync(db => db.Set<MonitorPoint>().SingleAsync(p => p.Id == _world.A.Point1.Id));
        point.DisplayName.ShouldBe("Shop (cloud)");
        point.Origin.ShouldBe(MonitorPoint.CloudOrigin);
        (await _app.InDbAsync(db => db.Set<MonitorPointState>().SingleAsync(s => s.MonitorPointId == point.Id))).Status.ShouldBe(Domain.Monitoring.PointStatus.Critical);
    }
}
