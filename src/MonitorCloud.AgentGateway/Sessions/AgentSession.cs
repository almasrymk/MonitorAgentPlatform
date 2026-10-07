using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentGateway.Transport;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>A live device stream after a successful Hello.</summary>
public sealed class AgentSession(Guid deviceId, Guid tenantId, Guid locationId, IAgentTransport transport, DateTimeOffset connectedAt) : IDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private long _lastMessageTicks = connectedAt.UtcTicks;

    public string SessionId { get; } = Guid.CreateVersion7().ToString("N");
    public Guid DeviceId { get; } = deviceId;
    public Guid TenantId { get; } = tenantId;
    public Guid LocationId { get; } = locationId;
    public DateTimeOffset ConnectedAt { get; } = connectedAt;
    public IAgentTransport Transport { get; } = transport;

    /// <summary>Why the gateway ended the session; null while open or when the agent closed it.</summary>
    public string? CloseCode { get; private set; }

    public CancellationToken Closed => _closed.Token;

    public DateTimeOffset LastMessageAt => new(Interlocked.Read(ref _lastMessageTicks), TimeSpan.Zero);

    public void Touch(DateTimeOffset at) => Interlocked.Exchange(ref _lastMessageTicks, at.UtcTicks);

    public Task SendAsync(CloudMessage message, CancellationToken cancellationToken) =>
        Transport.WriteAsync(message, cancellationToken).AsTask();

    /// <summary>Sends <c>Disconnect</c> (best effort) and ends the stream.</summary>
    public async Task DisconnectAsync(string code, string message, uint retryAfterSeconds, DateTimeOffset now)
    {
        if (CloseCode is not null)
            return;
        CloseCode = code;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SendAsync(Messages.Disconnect(code, message, retryAfterSeconds, now), timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or Grpc.Core.RpcException)
        {
            // The stream may already be gone.
        }

        await _closed.CancelAsync();
    }

    public void Dispose() => _closed.Dispose();
}

internal static class Messages
{
    public static CloudMessage Disconnect(string code, string message, uint retryAfterSeconds, DateTimeOffset now) => new()
    {
        MessageId = Guid.CreateVersion7().ToString("N"),
        SentAt = Timestamp.FromDateTimeOffset(now),
        Disconnect = new Disconnect { Code = code, Message = message, RetryAfterSeconds = retryAfterSeconds },
    };

    public static CloudMessage Ack(ulong sequence, DateTimeOffset now) => new()
    {
        MessageId = Guid.CreateVersion7().ToString("N"),
        SentAt = Timestamp.FromDateTimeOffset(now),
        Ack = new Ack { Sequence = sequence },
    };
}
