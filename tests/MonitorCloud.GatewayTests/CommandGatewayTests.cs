using System.Net;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Abstractions.Commands;
using MonitorCloud.Application.Commands;
using MonitorCloud.Application.Common;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Commands;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Infrastructure.Commands;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.SimulatedAgent;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;
using WireStatus = MonitorCloud.AgentProtocol.V1.CommandStatus;

namespace MonitorCloud.GatewayTests;

/// <summary>M11 acceptance: signed remote actions reach the agent, the agent's checks refuse bad ones, every command and result is audited.</summary>
[Collection(SqlCollection.Name)]
public sealed class CommandGatewayTests(SqlServerFixture sql) : IAsyncLifetime
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

    private ICommandSigner Signer => _app.Services.GetRequiredService<ICommandSigner>();

    private async Task<SimulatedAgent.SimulatedAgent> ConnectedAsync(bool allow = true)
    {
        var identity = new AgentIdentity(Device.Id, TestWorld.DeviceSecret, Device.Fingerprint, Device.Hostname, string.Empty);
        var agent = new SimulatedAgent.SimulatedAgent(identity, _channel) { Clock = _app.Clock };
        agent.Commands.LoadKeys(Signer.PublicKeysJson());
        agent.Commands.AllowRemoteActions = allow;
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

    /// <summary>Turns <c>features.remoteActions</c> on (or off) in the device configuration.</summary>
    private async Task SetRemoteActionsAsync(bool enabled)
    {
        using var admin = _app.ClientFor(_world.A.Administrator);
        var document = new
        {
            telemetry = new { sampleSeconds = 5 },
            thresholds = new
            {
                cpu = new { warningPercent = 80, criticalPercent = 95, forSeconds = 300, clearBelowPercent = 75 },
                ram = new { warningPercent = 80, criticalPercent = 95, forSeconds = 300, clearBelowPercent = 75 },
                disk = new { warningPercent = 85, criticalPercent = 92, forSeconds = 60 },
                tempC = new { critical = 85, forSeconds = 120 },
            },
            features = new { remoteActions = enabled },
        };
        (await admin.PutJsonAsync($"/api/v1/devices/{Device.Id}/configuration", document)).EnsureSuccessStatusCode();
        await DispatchAsync();
    }

    private async Task<DeviceCommandDto> RequestAsync(string type = "refresh-inventory", string? service = null)
    {
        using var admin = _app.ClientFor(_world.A.Administrator);
        var response = await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", new { type, service, reason = "Customer asked for fresh data" });
        var command = await response.ShouldBeOkAsync<DeviceCommandDto>(HttpStatusCode.Accepted);
        await DispatchAsync();
        return command;
    }

    private Task<DeviceCommand> StoredAsync(Guid id) => _app.InDbAsync(db => db.Set<DeviceCommand>().IgnoreQueryFilters().SingleAsync(c => c.Id == id));

    [Fact]
    public async Task A_connected_device_receives_runs_and_answers_a_signed_command()
    {
        await SetRemoteActionsAsync(true);
        await using var agent = await ConnectedAsync();

        var command = await RequestAsync("service-restart", "W3SVC");

        await Eventually(async () => (await StoredAsync(command.Id)).Status == Domain.Commands.CommandStatus.Succeeded, "the result");
        var stored = await StoredAsync(command.Id);
        stored.SentAt.ShouldNotBeNull();
        stored.Output.ShouldNotBeNull().ShouldContain("service-restart");
        agent.CommandResults.ShouldHaveSingleItem().Status.ShouldBe(WireStatus.Succeeded);

        using var viewer = _app.ClientFor(_world.A.ReportViewer);
        var history = await (await viewer.GetAsync(new Uri($"/api/v1/devices/{Device.Id}/commands", UriKind.Relative))).ShouldBeOkAsync<PagedResult<DeviceCommandDto>>();
        var item = history.Items.ShouldHaveSingleItem();
        (item.Type, item.Service, item.Status, item.RequestedByName).ShouldBe(("service-restart", "W3SVC", "Succeeded", _world.A.Administrator.FullName));

        var audit = await _app.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().Where(a => a.Action.StartsWith("device.command")).OrderBy(a => a.At).ToListAsync());
        audit.Select(a => a.Action).ShouldBe(["device.command.requested", "device.command.completed"], ignoreOrder: true);
        audit.Single(a => a.Action == "device.command.requested").Details.ShouldNotBeNull().ShouldContain("Customer asked for fresh data");
        audit.Single(a => a.Action == "device.command.completed").Success.ShouldBeTrue();
    }

    [Fact]
    public async Task An_offline_device_gets_the_command_after_Welcome_while_it_has_not_expired()
    {
        await SetRemoteActionsAsync(true);
        var command = await RequestAsync();
        (await StoredAsync(command.Id)).Status.ShouldBe(Domain.Commands.CommandStatus.Pending);

        await using var agent = await ConnectedAsync();

        await Eventually(async () => (await StoredAsync(command.Id)).Status == Domain.Commands.CommandStatus.Succeeded, "the result after Welcome");
    }

    [Fact]
    public async Task An_unanswered_command_expires_and_is_never_sent_afterwards()
    {
        await SetRemoteActionsAsync(true);
        var command = await RequestAsync();

        _app.Clock.Advance(TimeSpan.FromMinutes(7));
        (await _app.Services.GetRequiredService<CommandsJob>().RunOnceAsync(CancellationToken.None)).ShouldBe(1);
        (await StoredAsync(command.Id)).Status.ShouldBe(Domain.Commands.CommandStatus.Expired);
        (await _app.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().AnyAsync(a => a.Action == "device.command.expired"))).ShouldBeTrue();

        await using var agent = await ConnectedAsync();
        await Task.Delay(500);
        agent.CommandResults.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_local_switch_refuses_every_command_and_the_refusal_is_recorded()
    {
        await SetRemoteActionsAsync(true);
        await using var agent = await ConnectedAsync(allow: false);

        var command = await RequestAsync("restart-agent");

        await Eventually(async () => (await StoredAsync(command.Id)).Status == Domain.Commands.CommandStatus.Rejected, "the refusal");
        (await StoredAsync(command.Id)).Output.ShouldBe("Remote actions are turned off on this device.");
        var completed = await _app.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().SingleAsync(a => a.Action == "device.command.completed"));
        completed.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task The_cloud_refuses_commands_without_the_feature_permission_setting_or_valid_input()
    {
        using var admin = _app.ClientFor(_world.A.Administrator);
        object Body(string type = "refresh-inventory", string? service = null, string reason = "Checking the device now") => new { type, service, reason };

        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body())).ShouldBeProblemAsync(HttpStatusCode.Conflict, ErrorCodes.RemoteActionsDisabled);
        await SetRemoteActionsAsync(true);
        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body("format-disk"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body("service-stop"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body("service-stop", "bad;name"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body(reason: "short"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        using var technician = _app.ClientFor(_world.A.Technician);
        (await technician.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var starter = _app.ClientFor(_world.B.Administrator);
        await (await starter.PostJsonAsync($"/api/v1/devices/{_world.B.Device1.Id}/commands", Body())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
        (await (await starter.GetAsync(new Uri($"/api/v1/devices/{_world.B.Device1.Id}/remote-actions", UriKind.Relative))).ShouldBeOkAsync<RemoteActionsDto>())
            .ShouldBe(new RemoteActionsDto(false, "feature"));
        (await (await admin.GetAsync(new Uri($"/api/v1/devices/{Device.Id}/remote-actions", UriKind.Relative))).ShouldBeOkAsync<RemoteActionsDto>()).ShouldBe(new RemoteActionsDto(true, null));
        (await _app.InDbAsync(db => db.Set<DeviceCommand>().IgnoreQueryFilters().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task An_unlicensed_device_refuses_remote_actions_D19()
    {
        await SetRemoteActionsAsync(true);
        await _app.InDbAsync(async db =>
        {
            var state = await db.Set<DeviceState>().IgnoreQueryFilters().SingleAsync(s => s.DeviceId == Device.Id);
            state.SetLicense(LicenseStateValue.Unlicensed, _app.Clock.GetUtcNow(), TimeSpan.FromDays(14));
            return await db.SaveChangesAsync();
        });
        using var admin = _app.ClientFor(_world.A.Administrator);

        await (await admin.PostJsonAsync($"/api/v1/devices/{Device.Id}/commands", new { type = "refresh-inventory", reason = "Checking the device now" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, ErrorCodes.DeviceUnlicensed);
        (await (await admin.GetAsync(new Uri($"/api/v1/devices/{Device.Id}/remote-actions", UriKind.Relative))).ShouldBeOkAsync<RemoteActionsDto>())
            .ShouldBe(new RemoteActionsDto(false, "unlicensed"));
    }

    [Fact]
    public async Task Enrollment_hands_out_the_public_key_that_verifies_the_commands()
    {
        var jwks = System.Text.Json.JsonDocument.Parse(Signer.PublicKeysJson()).RootElement.GetProperty("keys");
        var key = jwks.EnumerateArray().ShouldHaveSingleItem();
        (key.GetProperty("kty").GetString(), key.GetProperty("crv").GetString(), key.GetProperty("alg").GetString(), key.GetProperty("kid").GetString())
            .ShouldBe(("EC", "P-256", "ES256", Signer.KeyId));
        key.TryGetProperty("d", out _).ShouldBeFalse("the private part never leaves the cloud");
    }
}

/// <summary>The agent-side checks of 05 section 9, one by one (the real agent implements the same rules, AG-13).</summary>
public sealed class CommandVerifierTests
{
    private readonly TestClock _clock = new();
    private readonly Guid _device = Guid.CreateVersion7();
    private readonly System.Security.Cryptography.ECDsa _key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    private const string Kid = "test-key";

    private CommandVerifier Verifier()
    {
        var p = _key.ExportParameters(false);
        var jwks = System.Text.Json.JsonSerializer.Serialize(new { keys = new[] { new { kty = "EC", crv = "P-256", kid = Kid, x = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(p.Q.X), y = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(p.Q.Y) } } });
        var verifier = new CommandVerifier(_device, _clock);
        verifier.LoadKeys(jwks);
        return verifier;
    }

    private Command Signed(Guid? device = null, string type = "refresh-inventory", TimeSpan? lifetime = null, string nonce = "0123456789abcdef0123456789abcdef", string kid = Kid)
    {
        var id = Guid.CreateVersion7();
        var expires = DateTimeOffset.FromUnixTimeMilliseconds(_clock.GetUtcNow().Add(lifetime ?? TimeSpan.FromMinutes(5)).ToUnixTimeMilliseconds());
        var payload = DeviceCommand.Payload(id, type, "{}", expires, nonce, device ?? _device);
        var signature = _key.SignData(System.Text.Encoding.UTF8.GetBytes(payload), System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new Command { CommandId = id.ToString("N"), Type = type, ParametersJson = "{}", ExpiresAt = Timestamp.FromDateTimeOffset(expires), Nonce = nonce, Signature = ByteString.CopyFrom(signature), KeyId = kid };
    }

    [Fact]
    public void A_valid_command_passes() => Verifier().Check(Signed()).ShouldBeNull();

    [Fact]
    public void A_tampered_type_fails_the_signature()
    {
        var command = Signed();
        command.Type = "restart-agent";

        Verifier().Check(command).ShouldBe((WireStatus.Rejected, "Invalid signature."));
    }

    [Fact]
    public void A_command_signed_for_another_device_is_refused() =>
        Verifier().Check(Signed(device: Guid.CreateVersion7())).ShouldBe((WireStatus.Rejected, "Invalid signature."));

    [Fact]
    public void An_expired_command_is_refused()
    {
        var command = Signed(lifetime: TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));

        Verifier().Check(command).ShouldBe((WireStatus.Expired, "The command has expired."));
    }

    [Fact]
    public void An_expiry_more_than_5_minutes_ahead_is_refused() =>
        Verifier().Check(Signed(lifetime: TimeSpan.FromMinutes(30))).ShouldBe((WireStatus.Rejected, "The expiry is more than 5 minutes ahead."));

    [Fact]
    public void A_replayed_nonce_is_refused_for_10_minutes()
    {
        var verifier = Verifier();
        verifier.Check(Signed()).ShouldBeNull();

        verifier.Check(Signed()).ShouldBe((WireStatus.Rejected, "Replayed nonce."));
    }

    [Fact]
    public void An_unknown_key_and_the_local_switch_refuse()
    {
        Verifier().Check(Signed(kid: "other")).ShouldBe((WireStatus.Rejected, "Unknown signing key."));
        var off = Verifier();
        off.AllowRemoteActions = false;
        off.Check(Signed()).ShouldBe((WireStatus.Rejected, "Remote actions are turned off on this device."));
    }
}
