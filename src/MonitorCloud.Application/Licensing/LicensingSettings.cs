namespace MonitorCloud.Application.Licensing;

/// <summary>The <c>Licensing</c> configuration section (04 section 2).</summary>
public sealed class LicensingSettings
{
    public const string Section = "Licensing";

    public string Mode { get; set; } = "Fake";
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ProductCode { get; set; } = "000001";
    public int SyncIntervalSeconds { get; set; } = 60;
    public int FullReconcileHours { get; set; } = 24;
    public int EntitlementStaleAfterMinutes { get; set; } = 30;
    public int UnlicensedGraceDays { get; set; } = 14;
    public int FailOpenHours { get; set; } = 72;
    public int ExpiringSoonDays { get; set; } = 30;
    public bool SyncEnabled { get; set; } = true;
    public string FakeDataPath { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public bool IsLive => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);
}
