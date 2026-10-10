namespace MonitorCloud.Infrastructure.Persistence;

/// <summary>One SQL schema per module (01 section 2).</summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Tenancy = "tenancy";
    public const string Licensing = "licensing";
    public const string Devices = "devices";
    public const string Telemetry = "telemetry";
    public const string Monitoring = "monitoring";
    public const string Notifications = "notifications";
    public const string Configuration = "config";
    public const string Reports = "reports";
    public const string Archive = "archive";
    public const string Audit = "audit";
    public const string Messaging = "messaging";
    public const string Media = "media";
    public const string Commands = "commands";

    public static readonly IReadOnlyList<string> All =
        [Identity, Tenancy, Licensing, Devices, Telemetry, Monitoring, Notifications, Configuration, Reports, Archive, Audit, Messaging, Media, Commands];
}
