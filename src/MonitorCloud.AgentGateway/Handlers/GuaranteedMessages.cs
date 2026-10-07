using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary>
/// Sequence rules for guaranteed messages (05 section 2, rules 4 and 5): a sequence at or below the last accepted
/// one is a duplicate and is acknowledged at once; otherwise the handler processes it and acknowledges after commit.
/// </summary>
internal static class GuaranteedMessages
{
    public static async Task<bool> AcceptAsync(AgentSession session, AgentMessage message, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (message.Sequence == 0)
            return true;
        if (!session.TryAccept(message.Sequence))
        {
            await session.SendAsync(Messages.Ack(message.Sequence, clock.GetUtcNow()), cancellationToken);
            return false;
        }

        return true;
    }

    public static Task AckAsync(AgentSession session, AgentMessage message, TimeProvider clock, CancellationToken cancellationToken) =>
        message.Sequence == 0 ? Task.CompletedTask : session.SendAsync(Messages.Ack(message.Sequence, clock.GetUtcNow()), cancellationToken);
}
