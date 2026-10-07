namespace MonitorCloud.Application.Devices.Contracts;

/// <summary>The <c>Agent</c> configuration section: what enrolling agents are told, and enrollment limits.</summary>
public sealed class AgentSettings
{
    public const string Section = "Agent";

    /// <summary>The gRPC gateway URL handed to agents at enrollment.</summary>
    public string GatewayUrl { get; set; } = "http://localhost:5300";

    /// <summary>The public API base used in install commands.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5300";

    public int SupportedProtocolMin { get; set; } = 1;
    public int SupportedProtocolMax { get; set; } = 1;
    public int EnrollPerFingerprintPerHour { get; set; } = 5;

    /// <summary>Licence refresh, grace period and attempt clean-up (off in tests, which run the cycle directly).</summary>
    public bool BackgroundJobsEnabled { get; set; } = true;
}
