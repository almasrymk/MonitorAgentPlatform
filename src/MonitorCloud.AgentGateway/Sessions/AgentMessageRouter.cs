using Microsoft.Extensions.Logging;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>Handles one kind of agent message (05 section 3). Telemetry, inventory and issues register from M5.</summary>
public interface IAgentMessageHandler
{
    AgentMessage.BodyOneofCase Kind { get; }

    Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Sends each agent message to the handler of its kind. Heartbeats only refresh presence (done by the session).
/// Guaranteed messages without a handler are not acknowledged, so the agent keeps them until a handler exists.
/// </summary>
public sealed partial class AgentMessageRouter(IEnumerable<IAgentMessageHandler> handlers, ILogger<AgentMessageRouter> logger)
{
    private readonly Dictionary<AgentMessage.BodyOneofCase, IAgentMessageHandler> _handlers = handlers.ToDictionary(h => h.Kind);

    public Task RouteAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (message.BodyCase is AgentMessage.BodyOneofCase.Heartbeat or AgentMessage.BodyOneofCase.Hello)
            return Task.CompletedTask;
        if (_handlers.TryGetValue(message.BodyCase, out var handler))
            return handler.HandleAsync(session, message, cancellationToken);
        LogUnhandled(logger, message.BodyCase.ToString(), session.DeviceId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No handler for {Kind} from device {DeviceId}; not acknowledged")]
    private static partial void LogUnhandled(ILogger logger, string kind, Guid deviceId);
}
