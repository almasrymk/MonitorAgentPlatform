using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Commands;
using MonitorCloud.Domain.Commands;
using MonitorCloud.SharedKernel;
using DomainStatus = MonitorCloud.Domain.Commands.CommandStatus;
using WireStatus = MonitorCloud.AgentProtocol.V1.CommandStatus;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary>Sends the device's open, signed commands (05 section 9) and marks them sent.</summary>
public sealed class CommandPusher(IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task<int> PushAsync(AgentSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var pending = await sender.Send(new GetPendingCommandsQuery(session.DeviceId, session.TenantId), cancellationToken);
        if (pending.IsFailure || pending.Value.Count == 0)
            return 0;
        foreach (var command in pending.Value)
        {
            await session.SendAsync(new CloudMessage
            {
                MessageId = Guid.CreateVersion7().ToString("N"),
                SentAt = Timestamp.FromDateTimeOffset(clock.GetUtcNow()),
                Command = new Command
                {
                    CommandId = command.Id.ToString("N"),
                    Type = command.Type,
                    ParametersJson = command.ParametersJson,
                    ExpiresAt = Timestamp.FromDateTimeOffset(command.ExpiresAt),
                    Nonce = command.Nonce,
                    Signature = ByteString.CopyFrom(command.Signature),
                    KeyId = command.KeyId,
                },
            }, cancellationToken);
        }

        await sender.Send(new MarkCommandsSentCommand(session.DeviceId, session.TenantId, [.. pending.Value.Select(c => c.Id)]), cancellationToken);
        return pending.Value.Count;
    }
}

/// <summary>A new command of a connected device is sent at once; an offline device gets it after its next Welcome.</summary>
public sealed class PushRequestedCommands(IAgentSessionRegistry registry, CommandPusher pusher) : IIntegrationEventHandler<DeviceCommandRequestedV1>
{
    public async Task HandleAsync(DeviceCommandRequestedV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (registry.Find(integrationEvent.DeviceId) is { } session)
            await pusher.PushAsync(session, cancellationToken);
    }
}

/// <summary><c>CommandResult</c> (guaranteed) -> <see cref="RecordCommandResultCommand"/>; acknowledged unless the database failed.</summary>
public sealed class CommandResultHandler(IServiceScopeFactory scopes, TimeProvider clock) : IAgentMessageHandler
{
    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.CommandResult;

    public static DomainStatus Map(WireStatus status) => status switch
    {
        WireStatus.Succeeded => DomainStatus.Succeeded,
        WireStatus.Rejected => DomainStatus.Rejected,
        WireStatus.Expired => DomainStatus.Expired,
        _ => DomainStatus.Failed,
    };

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        var result = message.CommandResult;
        if (Guid.TryParse(result.CommandId, out var commandId))
        {
            await using var scope = scopes.CreateAsyncScope();
            var recorded = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new RecordCommandResultCommand(session.DeviceId, session.TenantId, commandId, Map(result.Status), string.IsNullOrEmpty(result.Output) ? null : result.Output), cancellationToken);
            // An unknown or already answered command is acknowledged too: resending it would never succeed.
            if (recorded.IsFailure && recorded.Error!.Kind is not (ErrorKind.Validation or ErrorKind.NotFound or ErrorKind.Conflict))
                return;
        }

        await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
    }
}
