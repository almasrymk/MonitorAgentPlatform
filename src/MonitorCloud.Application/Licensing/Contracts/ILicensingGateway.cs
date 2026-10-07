using MonitorCloud.Application.Common;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Licensing.Contracts;

/// <summary>The Licensing Platform as seen by Monitor Cloud (04 section 2). Fake and HTTP adapters answer the same.</summary>
public interface ILicensingGateway
{
    Task<Result<SeatActivation>> ActivateSeatAsync(ActivateSeatRequest request, CancellationToken ct);

    Task<Result<SeatActivation>> RefreshSeatAsync(Guid licenseId, string deviceId, string? appVersion, string? os, CancellationToken ct);

    Task<Result> ReleaseSeatAsync(Guid licenseId, string deviceId, CancellationToken ct);

    Task<Result<CustomerEntitlements>> GetEntitlementsAsync(Guid licensingCustomerId, CancellationToken ct);

    Task<Result<PagedResult<LicensingCustomer>>> ListCustomersAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct);

    Task<Result<IReadOnlyList<LicensingPlan>>> ListPlansAsync(CancellationToken ct);

    Task<Result<ChangeFeedPage>> GetChangesAsync(string? cursor, int take, CancellationToken ct);

    Task<Result<string>> GetSigningKeysJsonAsync(CancellationToken ct);
}

public sealed record ActivateSeatRequest(string ProductKey, string DeviceId, string DeviceName, string AppVersion, string Os, string IdempotencyKey);

public sealed record SeatActivation(
    Guid LicenseId,
    Guid CustomerId,
    Guid SubscriptionId,
    string LicenseNumber,
    string PlanCode,
    IReadOnlyList<string> Features,
    DateTimeOffset? ExpiresAt,
    int? MaxActivations,
    int ActiveActivations,
    DateTimeOffset CheckAfter,
    DateTimeOffset OfflineValidUntil,
    string Token,
    string Kid,
    bool AlreadyActivated);

public sealed record LicensingCustomer(Guid Id, string Name, string? Email, string? Phone, string? Country, string Status, DateTimeOffset CreatedAt);

public sealed record LicensingSubscription(
    Guid Id, string Status, string PlanCode, string PlanName, int PlanVersion, IReadOnlyList<string> Features,
    DateTimeOffset StartDate, DateTimeOffset? EndDate, DateTimeOffset? TrialEndsAt);

public sealed record LicensingLicense(
    Guid Id, string LicenseNumber, Guid SubscriptionId, string Status, DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations, IReadOnlyList<string> Features);

public sealed record CustomerEntitlements(Guid CustomerId, string CustomerStatus, IReadOnlyList<LicensingSubscription> Subscriptions, IReadOnlyList<LicensingLicense> Licenses);

public sealed record PlanPrice(decimal Amount, string Currency);

public sealed record LicensingPlan(Guid Id, string Code, string Name, int Version, IReadOnlyList<string> Features, int? MaxActivations, int? DurationDays, PlanPrice Price);

public sealed record ChangeItem(Guid Id, string Type, DateTimeOffset OccurredAt, Guid? CustomerId, Guid? SubscriptionId, Guid? LicenseId);

public sealed record ChangeFeedPage(IReadOnlyList<ChangeItem> Items, string? NextCursor, bool HasMore);

/// <summary>Feature codes Monitor Cloud understands (04 section 1). Unknown codes are stored and ignored.</summary>
public static class Features
{
    public const string Monitoring = "monitoring";
    public const string Alerts = "alerts";
    public const string NotificationsEmail = "notifications.email";
    public const string NotificationsWebhook = "notifications.webhook";
    public const string MonitorPoints = "monitorpoints";
    public const string ReportsBasic = "reports.basic";
    public const string ReportsAdvanced = "reports.advanced";
    public const string Archive = "archive";
    public const string RemoteActions = "remote.actions";
}
