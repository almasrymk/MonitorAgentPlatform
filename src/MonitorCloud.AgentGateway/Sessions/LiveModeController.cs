using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Devices.Contracts;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>
/// Live mode (05 section 4): interest lasts 60 s; the first request sends <c>SetTelemetryMode(LIVE, 2 s, ttl 60 s)</c>
/// and later requests send it again every 30 s while interest remains. Without renewal the agent falls back by itself.
/// </summary>
public sealed class LiveModeController(IAgentSessionRegistry registry, TimeProvider clock) : ILiveModeControl
{
    public const uint IntervalSeconds = 2;
    public const uint TtlSeconds = 60;
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, (string SessionId, DateTimeOffset SentAt)> _sent = new();

    public async Task<bool> RequestLiveAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (registry.Find(deviceId) is not { } session)
            return false;
        var now = clock.GetUtcNow();
        if (_sent.TryGetValue(deviceId, out var last) && last.SessionId == session.SessionId && now - last.SentAt < ResendAfter)
            return true;
        _sent[deviceId] = (session.SessionId, now);
        await session.SendAsync(new CloudMessage
        {
            MessageId = Guid.CreateVersion7().ToString("N"),
            SentAt = Timestamp.FromDateTimeOffset(now),
            SetMode = new SetTelemetryMode { Mode = TelemetryMode.Live, LiveIntervalSeconds = IntervalSeconds, TtlSeconds = TtlSeconds },
        }, cancellationToken);
        return true;
    }
}
