using System.Collections.Concurrent;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Devices;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SimulatedAgent;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.GatewayTests;

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql";
}

/// <summary>The gateway tests of 09 section 5 (1-3, 7, 11) with <see cref="SimulatedAgent"/> against the in-memory server.</summary>
[Collection(SqlCollection.Name)]
public sealed class GatewayTests(SqlServerFixture sql) : IAsyncLifetime
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

    private AgentCloudClient Cloud => new(_app.CreateClient());

    private static AgentIdentity Identity(Device device) => new(device.Id, TestWorld.DeviceSecret, device.Fingerprint, device.Hostname, string.Empty);

    private async Task<SimulatedAgent.SimulatedAgent> AgentAsync(AgentIdentity identity) =>
        await Task.FromResult(new SimulatedAgent.SimulatedAgent(identity, _channel));

    private Task<DeviceState> StateAsync(Guid deviceId) => _app.InDbAsync(db => db.Set<DeviceState>().AsNoTracking().SingleAsync(s => s.DeviceId == deviceId));

    private IAgentSessionRegistry Registry => _app.Services.GetRequiredService<IAgentSessionRegistry>();

    private Task RunPresenceAsync() => _app.Services.GetRequiredService<PresenceMonitor>().RunOnceAsync(CancellationToken.None);

    private static async Task Eventually(Func<Task<bool>> condition, string what)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        throw new ShouldAssertException($"Timed out waiting for {what}.");
    }

    private async Task SetOfflineAsync(Guid deviceId) => await _app.InSystemScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var state = await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == deviceId);
        state.SetConnection(ConnectionState.Offline, _app.Clock.GetUtcNow(), TimeSpan.FromDays(14));
        await db.SaveChangesAsync();
    });

    private async Task<HubConnection> HubAsync(MonitorCloud.Domain.Identity.User user)
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

    // ---------------------------------------------------------------- 1

    [Fact]
    public async Task Enroll_token_connect_gives_Welcome_marks_the_device_online_and_broadcasts_the_state()
    {
        var key = _app.Services.GetRequiredService<FakeLicensingStore>().Snapshot().Licenses.Single(l => l.CustomerId == _world.LicensingCustomerOf(_world.A)).ProductKey;
        var identity = await Cloud.EnrollAsync(new EnrollRequest(key, "sim-gateway-0001", "SIM-GW-01", LocationCode: "LOC-ALPHA-TEST"));
        await using var hub = await HubAsync(_world.A.Administrator);
        var changes = new ConcurrentQueue<JsonElement>();
        hub.On<JsonElement>("deviceStateChanged", changes.Enqueue);
        await hub.InvokeAsync("SubscribeLocation", _world.A.Location1.Id);

        await using var agent = await AgentAsync(identity);
        var welcome = await agent.ConnectAsync(await Cloud.TokenAsync(identity));

        welcome.ShouldNotBeNull();
        welcome.HeartbeatSeconds.ShouldBe(30u);
        welcome.LicenseState.ShouldBe(LicenseState.Licensed);
        welcome.MaxBatchMinutes.ShouldBe(60u);
        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Online);
        Registry.Find(identity.DeviceId).ShouldNotBeNull();
        var device = await _app.InDbAsync(db => db.Set<Device>().AsNoTracking().SingleAsync(d => d.Id == identity.DeviceId));
        device.AgentVersion.ShouldBe(SimulatedAgent.SimulatedAgent.AgentVersion);
        await Eventually(() => Task.FromResult(changes.Any(c => c.GetProperty("deviceId").GetGuid() == identity.DeviceId && c.GetProperty("connection").GetString() == "Online")), "deviceStateChanged");
        (await _app.InDbAsync(db => db.Set<OutboxMessage>().AnyAsync(m => m.Type.Contains("DeviceCameOnlineV1")))).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- 2

    [Fact]
    public async Task A_first_message_that_is_not_Hello_closes_the_stream()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);
        agent.Open(await Cloud.TokenAsync(identity));

        await agent.HeartbeatAsync();
        await agent.Completion;

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe(AgentSessionHandler.HelloRequired);
    }

    [Fact]
    public async Task No_Hello_within_the_timeout_closes_the_stream()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);
        agent.Open(await Cloud.TokenAsync(identity));

        await agent.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe(AgentSessionHandler.HelloRequired);
    }

    [Fact]
    public async Task An_unsupported_protocol_version_is_refused()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);

        var welcome = await agent.ConnectAsync(await Cloud.TokenAsync(identity), protocolVersion: 9);

        welcome.ShouldBeNull();
        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("PROTOCOL_UNSUPPORTED");
        agent.Disconnected.RetryAfterSeconds.ShouldBe(0u);
    }

    [Fact]
    public async Task An_expired_token_is_unauthenticated()
    {
        var identity = Identity(_world.A.Device1);
        var token = await Cloud.TokenAsync(identity);
        _app.Clock.Advance(TimeSpan.FromMinutes(61));
        await using var agent = await AgentAsync(identity);

        (await agent.ConnectAsync(token)).ShouldBeNull();

        agent.Failure.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task A_user_token_is_rejected_by_the_gateway()
    {
        await using var agent = await AgentAsync(Identity(_world.A.Device1));

        (await agent.ConnectAsync(_app.TokenFor(_world.A.Administrator))).ShouldBeNull();

        agent.Failure.ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task A_revoked_credential_is_refused()
    {
        var identity = Identity(_world.A.Device1);
        var token = await Cloud.TokenAsync(identity);
        await _app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Set<DeviceCredential>().SingleAsync(c => c.DeviceId == identity.DeviceId)).Revoke(_app.Clock.GetUtcNow());
            await db.SaveChangesAsync();
        });
        await using var agent = await AgentAsync(identity);

        (await agent.ConnectAsync(token)).ShouldBeNull();

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("CREDENTIAL_REVOKED");
    }

    [Fact]
    public async Task A_retired_device_is_refused()
    {
        var identity = Identity(_world.A.Device1);
        var token = await Cloud.TokenAsync(identity);
        using (var admin = _app.ClientFor(_world.A.Administrator))
            (await admin.PostAsync(new Uri($"/api/v1/devices/{identity.DeviceId}/retire", UriKind.Relative), null)).EnsureSuccessStatusCode();
        await using var agent = await AgentAsync(identity);

        (await agent.ConnectAsync(token)).ShouldBeNull();

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("DEVICE_RETIRED");
        agent.Disconnected.RetryAfterSeconds.ShouldBe(0u);
    }

    [Fact]
    public async Task An_archived_tenant_is_refused()
    {
        var identity = Identity(_world.B.Device1);
        var token = await Cloud.TokenAsync(identity);
        using (var platform = _app.ClientFor(_world.PlatformAdmin))
            (await platform.PostJsonAsync($"/api/v1/platform/tenants/{_world.B.Id}/archive", new { reason = "Contract ended" })).EnsureSuccessStatusCode();
        await using var agent = await AgentAsync(identity);

        (await agent.ConnectAsync(token)).ShouldBeNull();

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("TENANT_ARCHIVED");
    }

    // ---------------------------------------------------------------- 3

    [Fact]
    public async Task A_second_connection_replaces_the_first_and_three_replacements_raise_the_clone_suspicion()
    {
        var identity = Identity(_world.A.Device1);
        var first = await AgentAsync(identity);
        (await first.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        var second = await AgentAsync(identity);
        (await second.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Disconnected.ShouldNotBeNull().Code.ShouldBe(AgentSessionHandler.DuplicateSession);
        first.Disconnected.RetryAfterSeconds.ShouldBe(0u);
        Registry.Find(identity.DeviceId)!.SessionId.ShouldBe(second.Received.First(m => m.Welcome is not null).Welcome.SessionId);

        var agents = new List<SimulatedAgent.SimulatedAgent> { first, second };
        for (var i = 0; i < 2; i++)
        {
            var next = await AgentAsync(identity);
            (await next.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();
            agents.Add(next);
        }

        (await _app.InDbAsync(db => db.Set<MonitorCloud.Domain.Audit.AuditRecord>().AnyAsync(a => a.Action == "device.possible_clone" && a.EntityId == identity.DeviceId.ToString()))).ShouldBeTrue();
        (await _app.InDbAsync(db => db.Set<OutboxMessage>().AnyAsync(m => m.Type.Contains("DeviceCloneSuspectedV1")))).ShouldBeTrue();
        foreach (var agent in agents)
            await agent.DisposeAsync();
    }

    // ---------------------------------------------------------------- 7

    [Fact]
    public async Task Missing_heartbeats_close_the_session_and_mark_the_device_offline()
    {
        var identity = Identity(_world.A.Device1);
        await SetOfflineAsync(identity.DeviceId);
        await using var agent = await AgentAsync(identity);
        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        _app.Clock.Advance(TimeSpan.FromSeconds(60));
        await agent.HeartbeatAsync();
        await Eventually(() => Task.FromResult(Registry.Find(identity.DeviceId)!.LastMessageAt == _app.Clock.GetUtcNow()), "the heartbeat");
        _app.Clock.Advance(TimeSpan.FromSeconds(60));
        await RunPresenceAsync();
        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Online, "two intervals are not enough");

        _app.Clock.Advance(TimeSpan.FromSeconds(31));
        await RunPresenceAsync();

        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Offline);
        await agent.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("HEARTBEAT_TIMEOUT");
        (await _app.InDbAsync(db => db.Set<OutboxMessage>().CountAsync(m => m.Type.Contains("DeviceWentOfflineV1")))).ShouldBe(1);
    }

    [Fact]
    public async Task A_closed_stream_marks_the_device_offline_after_the_grace_period_unless_it_reconnects()
    {
        var identity = Identity(_world.A.Device1);
        var agent = await AgentAsync(identity);
        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        await agent.CloseAsync();
        await Eventually(() => Task.FromResult(Registry.Find(identity.DeviceId) is null), "the session to close");
        _app.Clock.Advance(TimeSpan.FromSeconds(10));
        await RunPresenceAsync();
        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Online, "inside the 15 s grace period");

        _app.Clock.Advance(TimeSpan.FromSeconds(6));
        await RunPresenceAsync();
        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Offline);
        await agent.DisposeAsync();

        // Reconnecting brings it back.
        await using var again = await AgentAsync(identity);
        (await again.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();
        (await StateAsync(identity.DeviceId)).Connection.ShouldBe(ConnectionState.Online);
    }

    [Fact]
    public async Task Goodbye_marks_the_device_offline_at_once_with_the_reason()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);
        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        await agent.GoodbyeAsync(GoodbyeReason.HostShuttingDown);

        await Eventually(async () => (await StateAsync(identity.DeviceId)).Connection == ConnectionState.Offline, "offline after Goodbye");
        var events = await _app.InDbAsync(db => db.Set<OutboxMessage>().Where(m => m.Type.Contains("DeviceWentOfflineV1")).Select(m => m.Payload).ToListAsync());
        events.ShouldHaveSingleItem().ShouldContain("HostShuttingDown");
        _app.Clock.Advance(TimeSpan.FromSeconds(20));
        await RunPresenceAsync();
        (await _app.InDbAsync(db => db.Set<OutboxMessage>().CountAsync(m => m.Type.Contains("DeviceWentOfflineV1")))).ShouldBe(1, "no second offline event after the grace period");
    }

    [Fact]
    public async Task The_presence_monitor_writes_the_last_contact_of_connected_devices()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);
        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        _app.Clock.Advance(TimeSpan.FromSeconds(20));
        await RunPresenceAsync();

        (await StateAsync(identity.DeviceId)).LastSeenAt!.Value.ShouldBe(_app.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMilliseconds(5));
    }

    // ---------------------------------------------------------------- 11

    [Fact]
    public async Task A_token_whose_tenant_does_not_own_the_device_cannot_open_a_session()
    {
        // A correctly signed token that names tenant B for a device of tenant A (a forged combination).
        var token = _app.Services.GetRequiredService<IDeviceTokenService>().CreateDeviceToken(_world.A.Device1.Id, _world.B.Id, _world.A.Device1.Fingerprint, _app.Clock.GetUtcNow()).Token;
        await SetOfflineAsync(_world.A.Device1.Id);
        await using var agent = await AgentAsync(Identity(_world.A.Device1));

        (await agent.ConnectAsync(token)).ShouldBeNull();

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe("DEVICE_RETIRED");
        (await StateAsync(_world.A.Device1.Id)).Connection.ShouldBe(ConnectionState.Offline);
        Registry.Find(_world.A.Device1.Id).ShouldBeNull();
    }

    [Fact]
    public async Task Hello_cannot_move_a_device_to_another_tenant_and_only_updates_its_own_row()
    {
        var identity = Identity(_world.A.Device1) with { Hostname = "RENAMED-HOST" };
        await using var agent = await AgentAsync(identity);

        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        var rows = await _app.InDbAsync(db => db.Set<Device>().AsNoTracking().Where(d => d.Hostname == "RENAMED-HOST").ToListAsync());
        rows.ShouldHaveSingleItem().TenantId.ShouldBe(_world.A.Id);
        rows[0].Name.ShouldBe(_world.A.Device1.Name, "Hello keeps the name given in the portal");
        (await _app.InDbAsync(db => db.Set<Device>().AsNoTracking().SingleAsync(d => d.Id == _world.B.Device1.Id))).Hostname.ShouldBe(_world.B.Device1.Hostname);
    }

    // ---------------------------------------------------------------- live hub scope

    [Fact]
    public async Task Live_subscriptions_are_checked_against_the_callers_scope()
    {
        await using var b = await HubAsync(_world.B.Administrator);
        (await Should.ThrowAsync<HubException>(() => b.InvokeAsync("SubscribeLocation", _world.A.Location1.Id))).Message.ShouldContain("AUTH_FORBIDDEN");
        await Should.ThrowAsync<HubException>(() => b.InvokeAsync("SubscribeDevice", _world.A.Device1.Id));
        await Should.ThrowAsync<HubException>(() => b.InvokeAsync("SubscribePlatform"));
        await Should.ThrowAsync<HubException>(() => b.InvokeAsync("SubscribeTenant", _world.A.Id));
        await b.InvokeAsync("SubscribeTenant", (Guid?)null);

        await using var restricted = await HubAsync(_world.A.RestrictedManager);
        await restricted.InvokeAsync("SubscribeDevice", _world.A.Device1.Id);
        await Should.ThrowAsync<HubException>(() => restricted.InvokeAsync("SubscribeDevice", _world.A.Device2.Id));

        await using var platform = await HubAsync(_world.PlatformAdmin);
        await platform.InvokeAsync("SubscribePlatform");
        await platform.InvokeAsync("SubscribeTenant", (Guid?)_world.A.Id);
    }

    [Fact]
    public async Task The_hub_needs_a_signed_in_user()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_app.Server.BaseAddress, "/hubs/live"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        await Should.ThrowAsync<HttpRequestException>(() => hub.StartAsync());
        await hub.DisposeAsync();
    }

    // ---------------------------------------------------------------- shutdown

    [Fact]
    public async Task On_shutdown_agents_are_told_to_come_back_later()
    {
        var identity = Identity(_world.A.Device1);
        await using var agent = await AgentAsync(identity);
        (await agent.ConnectAsync(await Cloud.TokenAsync(identity))).ShouldNotBeNull();

        await new GatewayShutdown(Registry, _app.Clock).StopAsync(CancellationToken.None);
        await agent.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        agent.Disconnected.ShouldNotBeNull().Code.ShouldBe(AgentSessionHandler.ServerShutdown);
        agent.Disconnected.RetryAfterSeconds.ShouldBeInRange(5u, 30u);
    }
}
