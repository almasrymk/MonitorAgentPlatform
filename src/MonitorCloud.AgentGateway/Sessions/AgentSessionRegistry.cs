using System.Collections.Concurrent;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>Device id -> live session (05 section 2, presence). In memory; the interface allows a distributed one later.</summary>
public interface IAgentSessionRegistry
{
    /// <summary>Registers the session and returns the one it replaces (same device), if any.</summary>
    AgentSession? Register(AgentSession session);

    /// <summary>Removes the session if it is still the registered one for its device.</summary>
    bool Remove(AgentSession session);

    AgentSession? Find(Guid deviceId);

    IReadOnlyCollection<AgentSession> All { get; }

    /// <summary>Counts a replacement and returns how many happened inside the window.</summary>
    int CountReplacement(Guid deviceId, DateTimeOffset now, TimeSpan window);

    /// <summary>A stream closed without a Goodbye: the device goes offline after the grace period unless it reconnects.</summary>
    void MarkClosed(Guid deviceId, Guid tenantId, DateTimeOffset at);

    /// <summary>Closed streams older than <paramref name="before"/> whose device has no session; removed from the list.</summary>
    IReadOnlyList<(Guid DeviceId, Guid TenantId)> TakeExpiredClosures(DateTimeOffset before);
}

public sealed class AgentSessionRegistry : IAgentSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, (Guid TenantId, DateTimeOffset At)> _closed = new();
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _replacements = new();

    public IReadOnlyCollection<AgentSession> All => [.. _sessions.Values];

    public AgentSession? Register(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        AgentSession? previous = null;
        _sessions.AddOrUpdate(session.DeviceId, session, (_, existing) =>
        {
            previous = existing;
            return session;
        });
        _closed.TryRemove(session.DeviceId, out _);
        return previous;
    }

    public bool Remove(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _sessions.TryRemove(new KeyValuePair<Guid, AgentSession>(session.DeviceId, session));
    }

    public AgentSession? Find(Guid deviceId) => _sessions.GetValueOrDefault(deviceId);

    public int CountReplacement(Guid deviceId, DateTimeOffset now, TimeSpan window)
    {
        var queue = _replacements.GetOrAdd(deviceId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            queue.Enqueue(now);
            while (queue.Count > 0 && now - queue.Peek() > window)
                queue.Dequeue();
            return queue.Count;
        }
    }

    public void MarkClosed(Guid deviceId, Guid tenantId, DateTimeOffset at) => _closed[deviceId] = (tenantId, at);

    public IReadOnlyList<(Guid DeviceId, Guid TenantId)> TakeExpiredClosures(DateTimeOffset before)
    {
        var expired = new List<(Guid, Guid)>();
        foreach (var (deviceId, closure) in _closed)
        {
            if (closure.At > before)
                continue;
            if (_closed.TryRemove(new KeyValuePair<Guid, (Guid, DateTimeOffset)>(deviceId, closure)) && !_sessions.ContainsKey(deviceId))
                expired.Add((deviceId, closure.TenantId));
        }

        return expired;
    }
}
