namespace MonitorCloud.AgentGateway;

/// <summary>The <c>Gateway</c> configuration section (05 section 2).</summary>
public sealed class AgentGatewayOptions
{
    public const string Section = "Gateway";

    public int HelloTimeoutSeconds { get; set; } = 10;
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>Missed heartbeats before a silent session is closed (3 x heartbeat).</summary>
    public int MissedHeartbeats { get; set; } = 3;

    /// <summary>A closed stream marks the device Offline after this grace period (quick reconnects).</summary>
    public int OfflineGraceSeconds { get; set; } = 15;

    public int PresenceIntervalSeconds { get; set; } = 10;
    public int SessionLifetimeHours { get; set; } = 12;
    public int MaxBatchMinutes { get; set; } = 60;

    /// <summary>Replacements of one device's session within <see cref="CloneWindowMinutes"/> that count as a possible clone.</summary>
    public int CloneReplacements { get; set; } = 3;
    public int CloneWindowMinutes { get; set; } = 5;

    /// <summary>Off in tests, which run the presence cycle directly.</summary>
    public bool PresenceEnabled { get; set; } = true;
}
