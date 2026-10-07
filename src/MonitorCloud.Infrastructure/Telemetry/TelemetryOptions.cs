namespace MonitorCloud.Infrastructure.Telemetry;

/// <summary>The <c>Telemetry</c> configuration section (01 section 8, 02 section 5).</summary>
public sealed class TelemetryOptions
{
    public const string Section = "Telemetry";

    /// <summary>Batches waiting in the ingestion channel before readers wait (back-pressure).</summary>
    public int ChannelCapacity { get; set; } = 2000;

    public int FlushRows { get; set; } = 1000;
    public int FlushSeconds { get; set; } = 2;

    public int RetentionMinuteDays { get; set; } = 30;
    public int RetentionHourDays { get; set; } = 400;
    public int RetentionDiskDays { get; set; } = 400;

    /// <summary>Rollup every 5 minutes and retention once a day; off in tests, which run them directly.</summary>
    public bool JobsEnabled { get; set; } = true;
}
