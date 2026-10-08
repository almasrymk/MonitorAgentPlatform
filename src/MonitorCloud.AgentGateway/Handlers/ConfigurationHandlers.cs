using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Configuration;
using MonitorCloud.Application.Configuration.Contracts;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary>Builds and sends <c>ConfigUpdate</c> (05 section 8).</summary>
public sealed class ConfigurationPusher(IServiceScopeFactory scopes, TimeProvider clock)
{
    /// <summary>Sends the device's configuration when its version differs from <paramref name="appliedVersion"/> (on Welcome) or always (after a change).</summary>
    public async Task<bool> PushAsync(AgentSession session, int? appliedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetAgentConfigurationQuery(session.DeviceId, session.TenantId), cancellationToken);
        if (result.IsFailure || result.Value is not { } configuration || configuration.Version == appliedVersion)
            return false;
        await session.SendAsync(new CloudMessage
        {
            MessageId = Guid.CreateVersion7().ToString("N"),
            SentAt = Timestamp.FromDateTimeOffset(clock.GetUtcNow()),
            ConfigUpdate = new ConfigUpdate { Version = configuration.Version, JsonBrotli = Brotli.Compress(configuration.Json) },
        }, cancellationToken);
        return true;
    }
}

/// <summary>A configuration change of a connected device is pushed at once (gateway test 9).</summary>
public sealed class PushConfigurationChanges(IAgentSessionRegistry registry, ConfigurationPusher pusher) : IIntegrationEventHandler<DeviceConfigurationChangedV1>
{
    public async Task HandleAsync(DeviceConfigurationChangedV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (registry.Find(integrationEvent.DeviceId) is { } session)
            await pusher.PushAsync(session, null, cancellationToken);
    }
}

/// <summary><c>ConfigApplied</c> (guaranteed; processed also for restricted devices, rule 9) -> <see cref="RecordConfigAppliedCommand"/>.</summary>
public sealed class ConfigAppliedHandler(IServiceScopeFactory scopes, TimeProvider clock) : IAgentMessageHandler
{
    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.ConfigApplied;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        var applied = message.ConfigApplied;
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new RecordConfigAppliedCommand(session.DeviceId, session.TenantId, applied.Version, applied.Success, string.IsNullOrEmpty(applied.Error) ? null : applied.Error), cancellationToken);
        if (result.IsFailure && result.Error!.Kind != ErrorKind.Validation)
            return;
        await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
    }
}

internal static class Brotli
{
    public static Google.Protobuf.ByteString Compress(string json)
    {
        using var buffer = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            brotli.Write(bytes, 0, bytes.Length);
        }

        return Google.Protobuf.ByteString.CopyFrom(buffer.ToArray());
    }
}
