using MonitorCloud.Application.Common;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Licensing.Fake;

/// <summary>The <c>Fake</c> adapter of <see cref="ILicensingGateway"/> over <see cref="FakeLicensingStore"/>.</summary>
public sealed class FakeLicensingGateway(FakeLicensingStore store, TimeProvider clock) : ILicensingGateway
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(7);

    private Error? Unreachable()
    {
        if (store.Unavailable)
            return LicensingErrors.Unavailable;
        if (store.RateLimitNextCalls > 0)
        {
            store.RateLimitNextCalls--;
            return LicensingErrors.RateLimited;
        }

        return null;
    }

    public Task<Result<SeatActivation>> ActivateSeatAsync(ActivateSeatRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Unreachable() is { } down)
            return Task.FromResult<Result<SeatActivation>>(down);

        var result = store.Write<Result<SeatActivation>>((data, idempotent) =>
        {
            if (!string.IsNullOrEmpty(request.IdempotencyKey) && idempotent.TryGetValue(request.IdempotencyKey, out var replay))
                return replay;
            if (!LicensingProblem.IsValidDeviceId(request.DeviceId))
                return LicensingProblem.InvalidDevice;

            var license = data.Licenses.FirstOrDefault(l => Normalize(l.ProductKey) == Normalize(request.ProductKey));
            if (license is null)
                return LicensingProblem.InvalidLicense;
            if (Check(data, license) is { } rule)
                return rule;

            var already = license.Devices.Contains(request.DeviceId, StringComparer.Ordinal);
            if (!already)
            {
                if (license.MaxActivations is { } max && license.Devices.Count >= max)
                    return LicensingProblem.ActivationLimitReached;
                license.Devices.Add(request.DeviceId);
            }

            var activation = Respond(data, license, request.DeviceId, already);
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
                idempotent[request.IdempotencyKey] = activation;
            return activation;
        });
        return Task.FromResult(result);
    }

    public Task<Result<SeatActivation>> RefreshSeatAsync(Guid licenseId, string deviceId, string? appVersion, string? os, CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result<SeatActivation>>(down);
        var result = store.Write<Result<SeatActivation>>((data, _) =>
        {
            var license = data.Licenses.FirstOrDefault(l => l.Id == licenseId);
            if (license is null)
                return LicensingProblem.InvalidLicense;
            if (Check(data, license) is { } rule)
                return rule;
            if (!license.Devices.Contains(deviceId, StringComparer.Ordinal))
                return LicensingProblem.DeviceNotActivated;
            return Respond(data, license, deviceId, alreadyActivated: false);
        });
        return Task.FromResult(result);
    }

    public Task<Result> ReleaseSeatAsync(Guid licenseId, string deviceId, CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result>(down);
        var result = store.Write<Result>((data, _) =>
        {
            var license = data.Licenses.FirstOrDefault(l => l.Id == licenseId);
            if (license is null)
                return LicensingProblem.InvalidLicense;
            return license.Devices.Remove(deviceId) ? Result.Success() : LicensingProblem.DeviceNotActivated;
        });
        return Task.FromResult(result);
    }

    public Task<Result<CustomerEntitlements>> GetEntitlementsAsync(Guid licensingCustomerId, CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result<CustomerEntitlements>>(down);
        var result = store.Read<Result<CustomerEntitlements>>((data, _) =>
        {
            var customer = data.Customers.FirstOrDefault(c => c.Id == licensingCustomerId);
            if (customer is null)
                return LicensingErrors.CustomerNotFound;
            var subscriptions = data.Subscriptions.Where(s => s.CustomerId == customer.Id)
                .Select(s =>
                {
                    var plan = data.Plans.First(p => p.Code == s.PlanCode);
                    return new LicensingSubscription(s.Id, s.Status, plan.Code, plan.Name, plan.Version, [.. plan.Features], s.StartDate, s.EndDate, null);
                }).ToList();
            var licenses = data.Licenses.Where(l => l.CustomerId == customer.Id)
                .Select(l => new LicensingLicense(l.Id, l.LicenseNumber, l.SubscriptionId, l.Status, l.ExpiresAt, l.MaxActivations, l.Devices.Count, FeaturesOf(data, l)))
                .ToList();
            return new CustomerEntitlements(customer.Id, customer.Status, subscriptions, licenses);
        });
        return Task.FromResult(result);
    }

    public Task<Result<PagedResult<LicensingCustomer>>> ListCustomersAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result<PagedResult<LicensingCustomer>>>(down);
        var result = store.Read<Result<PagedResult<LicensingCustomer>>>((data, _) =>
        {
            var query = data.Customers.Where(c => updatedSince is null || c.CreatedAt >= updatedSince).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new LicensingCustomer(c.Id, c.Name, c.Email, null, c.Country, c.Status, c.CreatedAt)).ToList();
            return new PagedResult<LicensingCustomer>(items, query.Count, page, pageSize);
        });
        return Task.FromResult(result);
    }

    public Task<Result<IReadOnlyList<LicensingPlan>>> ListPlansAsync(CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result<IReadOnlyList<LicensingPlan>>>(down);
        var result = store.Read<Result<IReadOnlyList<LicensingPlan>>>((data, _) =>
            data.Plans.Select(p => new LicensingPlan(p.Id, p.Code, p.Name, p.Version, [.. p.Features], p.MaxActivations, p.DurationDays, new PlanPrice(p.Price, p.Currency))).ToList());
        return Task.FromResult(result);
    }

    public Task<Result<ChangeFeedPage>> GetChangesAsync(string? cursor, int take, CancellationToken ct)
    {
        if (Unreachable() is { } down)
            return Task.FromResult<Result<ChangeFeedPage>>(down);
        var result = store.Read<Result<ChangeFeedPage>>((_, changes) =>
        {
            var ordered = changes.OrderBy(c => c.OccurredAt).ThenBy(c => c.Id).ToList();
            var start = 0;
            if (!string.IsNullOrEmpty(cursor))
            {
                var index = ordered.FindIndex(c => c.Id.ToString("N") == cursor);
                if (index < 0)
                    return Error.Validation("VALIDATION_FAILED", "The cursor is not valid.");
                start = index + 1;
            }

            var page = ordered.Skip(start).Take(take).ToList();
            var next = page.Count == 0 ? cursor : page[^1].Id.ToString("N");
            return new ChangeFeedPage(page, next, start + page.Count < ordered.Count);
        });
        return Task.FromResult(result);
    }

    public Task<Result<string>> GetSigningKeysJsonAsync(CancellationToken ct) =>
        Task.FromResult(Unreachable() is { } down ? Result<string>.Failure(down) : Result<string>.Ok(store.SigningKeysJson()));

    private SeatActivation Respond(FakeLicensingData data, FakeLicense license, string deviceId, bool alreadyActivated)
    {
        var now = clock.GetUtcNow();
        var subscription = data.Subscriptions.First(s => s.Id == license.SubscriptionId);
        var features = FeaturesOf(data, license);
        var checkAfter = now.Add(CheckInterval);
        var offlineUntil = now.Add(CheckInterval).Add(OfflineGrace);
        if (license.ExpiresAt is { } exp && exp < offlineUntil)
            offlineUntil = exp;
        var token = store.Sign(license, deviceId, features, subscription.PlanCode, now, checkAfter, offlineUntil);
        return new SeatActivation(license.Id, license.CustomerId, license.SubscriptionId, license.LicenseNumber, subscription.PlanCode, features,
            license.ExpiresAt, license.MaxActivations, license.Devices.Count, checkAfter, offlineUntil, token, store.Kid, alreadyActivated);
    }

    /// <summary>The Licensing Platform's activation rules, in its order.</summary>
    private Error? Check(FakeLicensingData data, FakeLicense license)
    {
        var customer = data.Customers.FirstOrDefault(c => c.Id == license.CustomerId);
        if (customer is null || customer.Status != "Active")
            return LicensingProblem.InvalidLicense;
        if (license.Status == "Revoked")
            return LicensingProblem.Revoked;
        if (license.Status == "Suspended")
            return LicensingProblem.Suspended;
        var subscription = data.Subscriptions.First(s => s.Id == license.SubscriptionId);
        return subscription.Status switch
        {
            "Expired" => LicensingProblem.SubscriptionExpired,
            "Suspended" or "Cancelled" => LicensingProblem.SubscriptionInactive,
            _ when license.Status == "Expired" || (license.ExpiresAt is { } exp && exp <= clock.GetUtcNow()) => LicensingProblem.Expired,
            _ => null,
        };
    }

    private static List<string> FeaturesOf(FakeLicensingData data, FakeLicense license)
    {
        var subscription = data.Subscriptions.First(s => s.Id == license.SubscriptionId);
        return [.. data.Plans.First(p => p.Code == subscription.PlanCode).Features];
    }

    private static string Normalize(string? key) =>
        new((key ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
