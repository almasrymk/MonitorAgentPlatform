namespace MonitorCloud.Domain.Licensing;

public enum SubscriptionStatus
{
    None,
    Trial,
    Active,
    Suspended,
    Cancelled,
    Expired,
}

/// <summary>A subscription of the customer for the Monitor Agent product, as read from the Licensing Platform.</summary>
public sealed record LicensingSubscriptionFacts(
    Guid Id, SubscriptionStatus Status, string PlanCode, string PlanName, IReadOnlyList<string> Features, DateTimeOffset StartDate, DateTimeOffset? EndDate);

/// <summary>A licence of the customer for the product.</summary>
public sealed record LicensingLicenseFacts(Guid Id, string Status, int? MaxActivations, int ActiveActivations);

/// <summary>The cached entitlement derived from the Licensing answer (04 section 4.3).</summary>
public sealed record EntitlementState(
    string? PlanCode,
    string? PlanName,
    SubscriptionStatus SubscriptionStatus,
    IReadOnlyList<string> Features,
    int? MaxDevices,
    int ActiveSeats,
    DateTimeOffset? StartsAt,
    DateTimeOffset? RenewsAt,
    IReadOnlyList<Guid> LicenseIds)
{
    public static EntitlementState Empty { get; } = new(null, null, SubscriptionStatus.None, [], 0, 0, null, null, []);
}

/// <summary>Derivation of 04 section 4.3, row by row.</summary>
public static class EntitlementRules
{
    public static SubscriptionStatus ParseStatus(string? status) =>
        Enum.TryParse<SubscriptionStatus>(status, ignoreCase: true, out var parsed) ? parsed : SubscriptionStatus.None;

    public static EntitlementState Derive(IReadOnlyCollection<LicensingSubscriptionFacts> subscriptions, IReadOnlyCollection<LicensingLicenseFacts> licenses)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(licenses);

        // Subscription used: Active or Trial with the latest end date (open-ended counts as latest);
        // otherwise the most recently ended one.
        var usable = subscriptions
            .Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial)
            .OrderByDescending(s => s.EndDate ?? DateTimeOffset.MaxValue)
            .ThenByDescending(s => s.StartDate)
            .FirstOrDefault();
        var used = usable ?? subscriptions
            .OrderByDescending(s => s.EndDate ?? s.StartDate)
            .ThenByDescending(s => s.StartDate)
            .FirstOrDefault();

        var active = licenses.Where(l => string.Equals(l.Status, "Active", StringComparison.OrdinalIgnoreCase)).ToList();
        int? maxDevices = active.Any(l => l.MaxActivations is null) ? null : active.Sum(l => l.MaxActivations!.Value);
        var activeSeats = active.Sum(l => l.ActiveActivations);

        if (used is null)
            return EntitlementState.Empty with { MaxDevices = maxDevices, ActiveSeats = activeSeats, LicenseIds = [.. active.Select(l => l.Id)] };

        return new EntitlementState(
            used.PlanCode,
            used.PlanName,
            used.Status,
            [.. used.Features.Select(f => f.Trim().ToLowerInvariant()).Where(f => f.Length > 0).Distinct().Order(StringComparer.Ordinal)],
            maxDevices,
            activeSeats,
            used.StartDate,
            used.EndDate,
            [.. active.Select(l => l.Id).Order()]);
    }
}
