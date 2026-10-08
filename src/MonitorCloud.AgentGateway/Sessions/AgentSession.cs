using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentGateway.Transport;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>A live device stream after a successful Hello.</summary>
public sealed class AgentSession(Guid deviceId, Guid tenantId, Guid locationId, IAgentTransport transport, DateTimeOffset connectedAt, ulong lastAccepted = 0, bool restricted = false) : IDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private long _lastMessageTicks = connectedAt.UtcTicks;
    private long _lastAccepted = (long)lastAccepted;
    private long _lastLiveTicks;

    /// <summary>Unlicensed after the grace period: only Hello, Heartbeat, Goodbye and ConfigApplied are processed (rule 9).</summary>
    public bool Restricted { get; } = restricted;

    /// <summary>Accepts a guaranteed sequence once; false for a duplicate (at or below the last accepted, rule 4).</summary>
    public bool TryAccept(ulong sequence)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _lastAccepted);
            if ((long)sequence <= current)
                return false;
            if (Interlocked.CompareExchange(ref _lastAccepted, (long)sequence, current) == current)
                return true;
        }
    }

    /// <summary>Throttles live samples to the requested interval.</summary>
    private int _clockSkew;

    public bool TryTakeLiveSlot(DateTimeOffset now, TimeSpan minInterval)
    {
        var last = Interlocked.Read(ref _lastLiveTicks);
        if (now.UtcTicks - last < minInterval.Ticks)
            return false;
        return Interlocked.CompareExchange(ref _lastLiveTicks, now.UtcTicks, last) == last;
    }

    /// <summary>Records whether the agent clock is off; true when this is the first reading of the session or it changed.</summary>
    public bool TrySetClockSkew(bool skewed)
    {
        var value = skewed ? 2 : 1;
        return Interlocked.Exchange(ref _clockSkew, value) != value;
    }

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
