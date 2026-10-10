namespace MonitorCloud.Application.Tenancy;

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Code,
    string Status,
    string Country,
    string City,
    string TimeZone,
    DateOnly CustomerSince,
    Guid? LicensingCustomerId,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason,
    DateTimeOffset? ArchivedAt,
    int Locations,
    string Version);

/// <summary>A card on the Customers screen. Plan, licence and device fields are filled from M2/M3.</summary>
public sealed record TenantCardDto(
    Guid Id,
    string Name,
    string Code,
    string Status,
    string City,
    string Country,
    DateOnly CustomerSince,
    int Locations,
    string? PlanCode,
    string? PlanName,
    int? Devices,
    int? Healthy,
    int? Warning,
    int? Critical,
    decimal? HealthScore,
    int? LicensesUsed,
    int? LicenseLimit,
    DateTimeOffset? NextRenewal,
    string? SubscriptionStatus = null,
    bool ExpiringSoon = false);

/// <summary>Customers tiles; <paramref name="NewLast30Days"/> is the delta of the Total tile ("+6 vs last 30 days").</summary>
public sealed record TenantsSummaryDto(int Total, int Active, int ExpiringSoon, int Suspended, int NewLast30Days);

public sealed record LocationDto(
    Guid Id,
    string Name,
    string Code,
    string? City,
    string? Country,
    string? AddressLine,
    string TimeZone,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    bool IsDefault,
    string Status,
    DateTimeOffset CreatedAt,
    string Version);

/// <summary>A location card; device counts come from the Devices module (M3).</summary>
public sealed record LocationCardDto(
    Guid Id,
    string Name,
    string Code,
    string? City,
    string? Country,
    bool IsDefault,
    string Status,
    int Devices,
    int Online,
    int Warning,
    int Critical,
    decimal? HealthScore);
