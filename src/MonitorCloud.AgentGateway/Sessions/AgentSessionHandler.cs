using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.AgentGateway.Transport;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Devices.Agent;
using MonitorCloud.SharedKernel;
using DomainOs = MonitorCloud.Domain.Devices.OsFamily;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>
/// Runs one device stream (05 section 2): Hello within the timeout, device checks, one session per device, Welcome,
/// then routes messages until the stream ends, the session is replaced or rotated, or the server stops.
/// </summary>
public sealed partial class AgentSessionHandler(
    IServiceScopeFactory scopes, IAgentSessionRegistry registry, AgentMessageRouter router, IOptions<AgentGatewayOptions> options, TimeProvider clock,
    ILogger<AgentSessionHandler> logger)
{
    public const string HelloRequired = "HELLO_REQUIRED";
    public const string DuplicateSession = "DUPLICATE_SESSION";
    public const string SessionRotate = "SESSION_ROTATE";
    public const string ServerShutdown = "SERVER_SHUTDOWN";

    public async Task RunAsync(IAgentTransport transport, Guid deviceId, Guid tenantId, CancellationToken callCancelled)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var settings = options.Value;

        var hello = await ReadHelloAsync(transport, TimeSpan.FromSeconds(settings.HelloTimeoutSeconds), callCancelled);
        if (hello is null)
        {
            await SendAsync(transport, Messages.Disconnect(HelloRequired, "The first message must be Hello.", 5, clock.GetUtcNow()), callCancelled);
            return;
        }

        var start = await SendCommandAsync(new OpenAgentSessionCommand(deviceId, tenantId, ToHello(hello), transport.RemoteAddress), callCancelled);
        if (start.IsFailure)
        {
            // Refused sessions are not retried, except when the reason is temporary (validation of a bad Hello).
            await SendAsync(transport, Messages.Disconnect(start.Error!.Code, start.Error.Message, 0, clock.GetUtcNow()), callCancelled);
            LogRefused(logger, deviceId, start.Error.Code);
            return;
        }

        var now = clock.GetUtcNow();
        using var session = new AgentSession(deviceId, tenantId, start.Value.LocationId, transport, now);
        var replaced = registry.Register(session);
        if (replaced is not null)
        {
            await replaced.DisconnectAsync(DuplicateSession, "A newer session of this device replaced this one.", 0, now);
            var count = registry.CountReplacement(deviceId, now, TimeSpan.FromMinutes(settings.CloneWindowMinutes));
            if (count >= settings.CloneReplacements)
                await SendCommandAsync(new ReportDuplicateSessionsCommand(deviceId, tenantId, count), callCancelled);
        }

        await session.SendAsync(Welcome(session, start.Value, settings, now), callCancelled);
        LogConnected(logger, deviceId, session.SessionId);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callCancelled, session.Closed);
        var goodbye = false;
        try
        {
            var lifetime = TimeSpan.FromHours(settings.SessionLifetimeHours);
            while (!linked.IsCancellationRequested)
            {
                var message = await transport.ReadAsync(linked.Token);
                if (message is null)
                    break;
                var at = clock.GetUtcNow();
                session.Touch(at);
                if (message.BodyCase == AgentMessage.BodyOneofCase.Goodbye)
                {
                    goodbye = true;
                    await SendCommandAsync(new MarkDevicesOfflineCommand([new OfflineDevice(deviceId, tenantId, message.Goodbye.Reason.ToString())]), callCancelled);
                    break;
                }

                await router.RouteAsync(session, message, linked.Token);
                if (at - session.ConnectedAt > lifetime)
                {
                    // Every device re-authenticates at least twice a day (05 section 1.2).
                    await session.DisconnectAsync(SessionRotate, "Session rotation.", (uint)Random.Shared.Next(1, 11), at);
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Replaced, rotated, shut down or the client went away.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Grpc.Core.RpcException)
        {
            LogStreamFailed(logger, ex, deviceId);
        }
        finally
        {
            if (registry.Remove(session) && !goodbye)
                registry.MarkClosed(deviceId, tenantId, clock.GetUtcNow());
            LogClosed(logger, deviceId, session.CloseCode ?? (goodbye ? "GOODBYE" : "STREAM_CLOSED"));
        }
    }

    private async Task<AgentMessage?> ReadHelloAsync(IAgentTransport transport, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            var first = await transport.ReadAsync(timer.Token);
            return first?.BodyCase == AgentMessage.BodyOneofCase.Hello ? first : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<TResult> SendCommandAsync<TResult>(IRequest<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }

    private static async Task SendAsync(IAgentTransport transport, CloudMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await transport.WriteAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or Grpc.Core.RpcException)
        {
            // The agent is gone.
        }
    }

    private static AgentHello ToHello(AgentMessage message)
    {
        var h = message.Hello;
        var os = h.OsFamily switch
        {
            AgentProtocol.V1.OsFamily.Windows => DomainOs.Windows,
            AgentProtocol.V1.OsFamily.Linux => DomainOs.Linux,
            AgentProtocol.V1.OsFamily.Macos => DomainOs.MacOS,
            _ => DomainOs.Other,
        };
        static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
        return new AgentHello((int)h.ProtocolVersion, Empty(h.AgentVersion), h.Hostname, os, Empty(h.OsName), Empty(h.OsVersion), Empty(h.Architecture),
            h.AppliedConfigVersion, Empty(h.LocalIp), Empty(h.MacAddress));
    }

    private static CloudMessage Welcome(AgentSession session, AgentSessionStart start, AgentGatewayOptions settings, DateTimeOffset now) => new()
    {
        MessageId = Guid.CreateVersion7().ToString("N"),
        SentAt = Timestamp.FromDateTimeOffset(now),
        Welcome = new Welcome
        {
            SessionId = session.SessionId,
            HeartbeatSeconds = (uint)settings.HeartbeatSeconds,
            LastReceivedSequence = start.LastReceivedSequence,
            ConfigVersion = start.ConfigVersion,
            LicenseState = start.Licensed ? LicenseState.Licensed : LicenseState.Unlicensed,
            ServerTime = Timestamp.FromDateTimeOffset(now),
            MaxBatchMinutes = (uint)settings.MaxBatchMinutes,
        },
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} connected (session {SessionId})")]
    private static partial void LogConnected(ILogger logger, Guid deviceId, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Device {DeviceId} session closed: {Code}")]
    private static partial void LogClosed(ILogger logger, Guid deviceId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} session refused: {Code}")]
    private static partial void LogRefused(ILogger logger, Guid deviceId, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Device {DeviceId} stream failed")]
    private static partial void LogStreamFailed(ILogger logger, Exception exception, Guid deviceId);
}
