namespace MonitorCloud.Application.Tenancy.Contracts;

/// <summary>Facts about a tenant other modules show (archive company details).</summary>
public interface ITenantFacts
{
    Task<DateOnly> CustomerSinceAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Platform-wide defaults from Platform Settings (06: /platform/settings) for the other modules.</summary>
public interface IPlatformDefaults
{
    /// <summary>The offline-alert delay of customers that did not set their own (default 2 minutes).</summary>
    Task<int> OfflineAlertDelayMinutesAsync(CancellationToken ct);
}