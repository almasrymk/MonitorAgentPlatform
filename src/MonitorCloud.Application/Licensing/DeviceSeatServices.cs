using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Licensing;

internal sealed class LicensingPolicy(IOptions<LicensingSettings> settings) : ILicensingPolicy
{
    public TimeSpan UnlicensedGrace => TimeSpan.FromDays(settings.Value.UnlicensedGraceDays);
}

internal sealed class DeviceSeats(IAppDbContext db, IReadDbContext read) : IDeviceSeats
{
    public async Task RecordLicensedAsync(Guid deviceId, Guid tenantId, string fingerprint, SeatActivation seat, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Set<DeviceLicense>().SingleOrDefaultAsync(l => l.DeviceId == deviceId, ct);
        if (existing is null)
            db.Set<DeviceLicense>().Add(DeviceLicense.Licensed(deviceId, tenantId, fingerprint, seat.LicenseId, seat.LicenseNumber, seat.Token, seat.Kid, seat.CheckAfter, seat.OfflineValidUntil, now));
        else
            existing.Renew(seat.LicenseId, seat.LicenseNumber, seat.Token, seat.Kid, seat.CheckAfter, seat.OfflineValidUntil, now);
    }

    public async Task<DeviceSeat?> FindAsync(Guid deviceId, CancellationToken ct) =>
        await read.Query<DeviceLicense>()
            .Where(l => l.DeviceId == deviceId)
            .Select(l => new DeviceSeat(l.DeviceId, l.LicenseId, l.LicenseNumber, l.State.ToString(), l.ReasonCode, l.Token, l.CheckAfter))
            .SingleOrDefaultAsync(ct);
}

internal sealed class EntitlementStatistics(IReadDbContext db, TimeProvider clock, IOptions<LicensingSettings> settings) : IEntitlementStatistics
{
    public async Task<IReadOnlyList<PlanDistribution>> DistributionAsync(CancellationToken ct) =>
        await db.Query<TenantEntitlement>()
            .Where(e => e.PlanCode != null && (e.SubscriptionStatus == SubscriptionStatus.Active || e.SubscriptionStatus == SubscriptionStatus.Trial))
            .GroupBy(e => new { e.PlanCode, e.PlanName })
            .Select(g => new PlanDistribution(g.Key.PlanCode!, g.Key.PlanName ?? g.Key.PlanCode!, g.Count()))
            .OrderByDescending(p => p.Customers)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpiringSubscription>> ExpiringAsync(int take, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now.AddDays(settings.Value.ExpiringSoonDays);
        var rows = await db.Query<TenantEntitlement>()
            .Where(e => e.RenewsAt != null && e.RenewsAt >= now && e.RenewsAt <= until
                && (e.SubscriptionStatus == SubscriptionStatus.Active || e.SubscriptionStatus == SubscriptionStatus.Trial))
            .OrderBy(e => e.RenewsAt)
            .Take(take)
            .Select(e => new { e.TenantId, e.PlanName, RenewsAt = e.RenewsAt!.Value })
            .ToListAsync(ct);
        return rows.Select(r => new ExpiringSubscription(r.TenantId, r.PlanName, r.RenewsAt, Math.Max(0, (int)Math.Ceiling((r.RenewsAt - now).TotalDays)))).ToList();
    }
}

/// <summary>A retired or unlicensed device gives its seat back to the Licensing Platform (04 section 4.2, D19).</summary>
internal sealed partial class ReleaseDeviceSeat(IAppDbContext db, ILicensingGateway licensing, TimeProvider clock, ILogger<ReleaseDeviceSeat> logger)
    : IIntegrationEventHandler<DeviceRetiredV1>, IIntegrationEventHandler<DeviceUnlicenseRequestedV1>
{
    public Task HandleAsync(DeviceRetiredV1 integrationEvent, CancellationToken cancellationToken) =>
        ReleaseAsync(integrationEvent.DeviceId, "DEVICE_RETIRED", cancellationToken);

    public Task HandleAsync(DeviceUnlicenseRequestedV1 integrationEvent, CancellationToken cancellationToken) =>
        ReleaseAsync(integrationEvent.DeviceId, "SEAT_RELEASED", cancellationToken);

    private async Task ReleaseAsync(Guid deviceId, string reason, CancellationToken ct)
    {
        var license = await db.Set<DeviceLicense>().SingleOrDefaultAsync(l => l.DeviceId == deviceId, ct);
        if (license is null || license.State == DeviceLicenseState.Unlicensed)
            return;
        var released = await licensing.ReleaseSeatAsync(license.LicenseId, license.DeviceFingerprint, ct);
        if (released.IsFailure && released.Error!.Kind == ErrorKind.Unavailable)
        {
            // Retried by the outbox: the Licensing Platform must count the seat as free.
            throw new InvalidOperationException($"Licensing Platform unavailable while releasing the seat of device {deviceId}.");
        }

        if (released.IsFailure)
            LogReleaseFailed(logger, deviceId, released.Error!.Code);
        license.Unlicense(reason, clock.GetUtcNow());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seat release for device {DeviceId} answered {Code}; the device is marked unlicensed")]
    private static partial void LogReleaseFailed(ILogger logger, Guid deviceId, string code);
}

/// <summary>
/// Refreshes due device licences (04 section 4.2): a valid answer renews the token; a rejection (expired, revoked,
/// suspended) makes the device Unlicensed; an unavailable Licensing Platform keeps the state and retries in an hour.
/// </summary>
public sealed class DeviceLicenseRefreshService(IAppDbContext db, IUnitOfWork unitOfWork, ILicensingGateway licensing, TimeProvider clock)
{
    public const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Set<DeviceLicense>()
            .Where(l => l.State == DeviceLicenseState.Licensed && l.CheckAfter <= now)
            .OrderBy(l => l.CheckAfter)
            .Take(BatchSize)
            .ToListAsync(ct);
        foreach (var license in due)
        {
            var result = await licensing.RefreshSeatAsync(license.LicenseId, license.DeviceFingerprint, null, null, ct);
            if (result.IsSuccess)
                license.Renew(result.Value.LicenseId, result.Value.LicenseNumber, result.Value.Token, result.Value.Kid, result.Value.CheckAfter, result.Value.OfflineValidUntil, now);
            else if (result.Error!.Kind is ErrorKind.Unavailable or ErrorKind.TooManyRequests)
                license.DeferCheck(now.AddHours(1));
            else
                license.Unlicense(result.Error.Code.Length > 64 ? result.Error.Code[..64] : result.Error.Code, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return due.Count;
    }
}
