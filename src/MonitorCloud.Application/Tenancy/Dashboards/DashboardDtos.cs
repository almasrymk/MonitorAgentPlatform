namespace MonitorCloud.Application.Tenancy.Dashboards;

/// <summary>A KPI tile: value and, where history exists, the change against the previous period.</summary>
public sealed record KpiTileDto(string Key, int Value, int? Delta, decimal? DeltaPercent, decimal? PercentOfTotal);

public sealed record HealthBreakdownDto(int Total, int Healthy, int Warning, int Critical, int Offline);

public sealed record NamedCountDto(string Name, int Count, decimal Percent);

/// <summary>One line of a trend chart.</summary>
public sealed record TrendSeriesDto(string Name, IReadOnlyList<TrendPointDto> Points);

public sealed record TrendPointDto(DateOnly Day, int Value);

/// <summary>An open alert in the "Recent Alerts" blocks of the dashboards.</summary>
public sealed record RecentAlertDto(Guid Id, DateTimeOffset At, string Severity, Guid? TenantId, string? CustomerName, Guid? DeviceId, string? DeviceName, string? LocationName, string Message);

/// <summary><paramref name="IssueTitle"/>: the most severe open alert of a critical or warning device (shown instead of the issue kind).</summary>
public sealed record ProblemDeviceDto(Guid Id, string Name, Guid LocationId, string? LocationName, string Issue, string? IssueTitle, string Health, string Connection, int OpenAlerts, DateTimeOffset? LastSeenAt);

public sealed record LocationStatusRowDto(Guid LocationId, string Name, int Total, int Online, int Healthy, int Warning, int Critical, decimal? HealthPercent);

public sealed record LicenseSummaryDto(int Licensed, int Unlicensed, string? PlanName, int Used, int? Limit, DateTimeOffset? RenewsAt);

public sealed record WorkspaceHeaderDto(Guid TenantId, string Name, string Status, string? PlanName, int Locations, int Devices, DateOnly CustomerSince);

public sealed record TenantDashboardDto(
    WorkspaceHeaderDto Header,
    IReadOnlyList<KpiTileDto> Tiles,
    IReadOnlyList<LocationCardDto> Locations,
    HealthBreakdownDto LocationsHealth,
    IReadOnlyList<TrendSeriesDto> IncidentTrend,
    IReadOnlyList<ProblemDeviceDto> TopProblematicDevices,
    IReadOnlyList<LocationStatusRowDto> DeviceStatusByLocation,
    IReadOnlyList<RecentAlertDto> RecentAlerts,
    LicenseSummaryDto License);

public sealed record ResourceAveragesDto(int OnlineDevices, decimal? Cpu, decimal? Ram, decimal? Disk, decimal? HealthScore);

public sealed record LocationSummaryDto(
    Guid Id, string Name, string Code, string? City, string? Country, string? AddressLine, string TimeZone, string? ContactName, string? ContactEmail, string? ContactPhone,
    bool IsDefault, string CustomerName, string? PlanName, DateOnly CustomerSince, DateTimeOffset? LastSyncAt);

public sealed record LocationDashboardDto(
    LocationSummaryDto Location,
    IReadOnlyList<KpiTileDto> Tiles,
    IReadOnlyList<TrendSeriesDto> IncidentTrend,
    IReadOnlyList<NamedCountDto> DevicesByOs,
    HealthBreakdownDto DeviceHealth,
    ResourceAveragesDto Resources,
    IReadOnlyList<ProblemDeviceDto> TopProblematicDevices,
    IReadOnlyList<RecentAlertDto> RecentAlerts);

public sealed record TopCustomerDto(Guid TenantId, string Name, int Devices, int Online, decimal? HealthScore);

public sealed record ExpiringSubscriptionDto(Guid TenantId, string Name, string? PlanName, DateTimeOffset RenewsAt, int DaysLeft, string Status);

public sealed record ActivityDto(DateTimeOffset At, string Action, string ActorName, string EntityType, string? Details, string? CustomerName);

public sealed record PlatformDashboardDto(
    IReadOnlyList<KpiTileDto> Tiles,
    IReadOnlyList<TrendSeriesDto> IncidentTrend,
    IReadOnlyList<NamedCountDto> IncidentsBySeverity,
    int ResolvedIncidents,
    IReadOnlyList<NamedCountDto> SubscriptionDistribution,
    IReadOnlyList<TopCustomerDto> TopCustomers,
    IReadOnlyList<ExpiringSubscriptionDto> ExpiringSubscriptions,
    IReadOnlyList<RecentAlertDto> RecentAlerts,
    HealthBreakdownDto DeviceHealth,
    IReadOnlyList<NamedCountDto> DevicesByOs,
    IReadOnlyList<ActivityDto> RecentActivity);
