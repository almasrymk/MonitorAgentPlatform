using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

/// <summary>
/// Cache of the customer's entitlement in the Licensing Platform (02 section 3). Never the source of truth.
/// Keyed by the tenant id.
/// </summary>
public sealed class TenantEntitlement : AggregateRoot, ITenantOwned
{
    private List<string> _features = [];
    private List<Guid> _licenseIds = [];

    private TenantEntitlement()
    {
    }

    public Guid TenantId { get; private set; }
    public string? PlanCode { get; private set; }
    public string? PlanName { get; private set; }
    public SubscriptionStatus SubscriptionStatus { get; private set; }
    public IReadOnlyList<string> Features => _features.AsReadOnly();
    public int? MaxDevices { get; private set; }
    public int ActiveSeats { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? RenewsAt { get; private set; }
    public IReadOnlyList<Guid> LicenseIds => _licenseIds.AsReadOnly();
    public DateTimeOffset? SyncedAt { get; private set; }
    public string? SyncError { get; private set; }

    public bool IsExpired => SubscriptionStatus == SubscriptionStatus.Expired;

    public static TenantEntitlement Create(Guid tenantId) =>
        new() { TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), Id = tenantId };

    /// <summary>
    /// Applies a freshly derived state. Raises <see cref="EntitlementChangedV1"/> only when plan, status, features,
    /// limit or dates actually change (04 section 4.3, rule 3). Seat counts alone do not raise the event.
    /// </summary>
    public bool Apply(EntitlementState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var changed = PlanCode != state.PlanCode
            || PlanName != state.PlanName
            || SubscriptionStatus != state.SubscriptionStatus
            || !_features.SequenceEqual(state.Features)
            || MaxDevices != state.MaxDevices
            || StartsAt != state.StartsAt
            || RenewsAt != state.RenewsAt;
        var previousPlan = PlanCode;
        var previousStatus = SubscriptionStatus;

        PlanCode = state.PlanCode;
        PlanName = state.PlanName;
        SubscriptionStatus = state.SubscriptionStatus;
        _features = [.. state.Features];
        MaxDevices = state.MaxDevices;
        ActiveSeats = state.ActiveSeats;
        StartsAt = state.StartsAt;
        RenewsAt = state.RenewsAt;
        _licenseIds = [.. state.LicenseIds];
        SyncedAt = now;
        SyncError = null;

        if (changed)
            Raise(new EntitlementChangedV1(TenantId, previousPlan, PlanCode, previousStatus, SubscriptionStatus, now));
        return changed;
    }

    public void RecordSyncError(string error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);
        SyncError = error.Length <= 500 ? error : error[..500];
        _ = now;
    }

    public bool Has(string feature) => _features.Contains(feature, StringComparer.OrdinalIgnoreCase);

    /// <summary>Renewal within <paramref name="window"/> for a usable subscription ("Expiring Soon").</summary>
    public bool IsExpiringSoon(DateTimeOffset now, TimeSpan window) =>
        SubscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.Trial && RenewsAt is { } end && end >= now && end <= now.Add(window);
}

public sealed record EntitlementChangedV1(
    Guid TenantId, string? PreviousPlanCode, string? PlanCode, SubscriptionStatus PreviousStatus, SubscriptionStatus Status, DateTimeOffset At) : DomainEvent(At);
