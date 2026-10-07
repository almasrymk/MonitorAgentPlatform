using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Licensing;

public sealed record SyncOutcome(bool Succeeded, int TenantsChecked, int TenantsChanged, string? Error);

/// <summary>
/// Keeps <c>TenantEntitlements</c> in line with the Licensing Platform (04 section 4.3). Runs in the system scope:
/// incremental sync from the change feed, and a full reconcile of every linked tenant.
/// </summary>
public sealed partial class LicensingSyncService(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    ILicensingGateway gateway,
    ILinkedTenantDirectory tenants,
    IAuditLogger audit,
    IOptions<LicensingSettings> settings,
    TimeProvider clock,
    ILogger<LicensingSyncService> logger) : IEntitlementRefresher
{
    public const int ChangePageSize = 200;

    /// <summary>One sync cycle: full reconcile when due, otherwise the change feed from the stored cursor.</summary>
    public async Task<SyncOutcome> RunAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync(ct);
        return state.FullReconcileDue(clock.GetUtcNow(), TimeSpan.FromHours(settings.Value.FullReconcileHours))
            ? await ReconcileAllAsync(ct)
            : await SyncChangesAsync(ct);
    }

    /// <summary>Reads the change feed; refreshes every customer it mentions; advances the cursor with the changes.</summary>
    public async Task<SyncOutcome> SyncChangesAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync(ct);
        var cursor = state.Cursor;
        var checkedCount = 0;
        var changedCount = 0;
        while (true)
        {
            var page = await gateway.GetChangesAsync(cursor, ChangePageSize, ct);
            if (page.IsFailure)
                return await FailAsync(state, page.Error!, ct);

            var customers = page.Value.Items.Select(i => i.CustomerId).OfType<Guid>().Distinct().ToList();
            var linked = await tenants.FindByLicensingCustomersAsync(customers, ct);
            foreach (var (customerId, tenantId) in linked)
            {
                var result = await RefreshCoreAsync(tenantId, customerId, ct);
                if (result.IsFailure)
                    return await FailAsync(state, result.Error!, ct);
                checkedCount++;
                changedCount += result.Value ? 1 : 0;
            }

            cursor = page.Value.NextCursor ?? cursor;
            // Cursor and entitlement changes are saved in the same transaction.
            state.Succeeded(cursor, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
            if (!page.Value.HasMore)
                return new SyncOutcome(true, checkedCount, changedCount, null);
        }
    }

    /// <summary>Reconciles every tenant linked to a Licensing customer.</summary>
    public async Task<SyncOutcome> ReconcileAllAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync(ct);
        var linked = await tenants.AllLinkedAsync(ct);
        var changedCount = 0;
        foreach (var (customerId, tenantId) in linked)
        {
            var result = await RefreshCoreAsync(tenantId, customerId, ct);
            if (result.IsFailure)
                return await FailAsync(state, result.Error!, ct);
            changedCount += result.Value ? 1 : 0;
        }

        // A full reconcile also moves the cursor to the end of the feed so the next cycle starts from "now".
        var cursor = state.Cursor;
        while (true)
        {
            var page = await gateway.GetChangesAsync(cursor, ChangePageSize, ct);
            if (page.IsFailure)
                return await FailAsync(state, page.Error!, ct);
            cursor = page.Value.NextCursor ?? cursor;
            if (!page.Value.HasMore)
                break;
        }

        state.Succeeded(cursor, clock.GetUtcNow(), fullReconcile: true);
        await unitOfWork.SaveChangesAsync(ct);
        LogReconciled(logger, linked.Count, changedCount);
        return new SyncOutcome(true, linked.Count, changedCount, null);
    }

    public async Task<bool> RefreshTenantAsync(Guid tenantId, Guid licensingCustomerId, CancellationToken ct)
    {
        var result = await RefreshCoreAsync(tenantId, licensingCustomerId, ct);
        if (result.IsFailure)
            return false;
        await unitOfWork.SaveChangesAsync(ct);
        return result.Value;
    }

    private async Task<Result<bool>> RefreshCoreAsync(Guid tenantId, Guid customerId, CancellationToken ct)
    {
        var answer = await gateway.GetEntitlementsAsync(customerId, ct);
        var entitlement = await db.Set<TenantEntitlement>().SingleOrDefaultAsync(e => e.TenantId == tenantId, ct);
        if (answer.IsFailure)
        {
            if (answer.Error!.Kind == ErrorKind.NotFound && entitlement is not null)
                entitlement.RecordSyncError(answer.Error.Code, clock.GetUtcNow());
            return answer.Error!.Kind == ErrorKind.NotFound ? false : answer.Error;
        }

        if (entitlement is null)
        {
            entitlement = TenantEntitlement.Create(tenantId);
            db.Set<TenantEntitlement>().Add(entitlement);
        }

        return entitlement.Apply(EntitlementMapping.Derive(answer.Value), clock.GetUtcNow());
    }

    private async Task<LicensingSyncState> LoadStateAsync(CancellationToken ct)
    {
        var state = await db.Set<LicensingSyncState>().SingleOrDefaultAsync(s => s.Id == LicensingSyncState.SingletonId, ct);
        if (state is null)
        {
            state = LicensingSyncState.Create();
            db.Set<LicensingSyncState>().Add(state);
        }

        return state;
    }

    /// <summary>Entitlements keep their last value; the failure is counted and audited (04 section 4.5).</summary>
    private async Task<SyncOutcome> FailAsync(LicensingSyncState state, Error error, CancellationToken ct)
    {
        unitOfWork.DiscardChanges();
        var tracked = await LoadStateAsync(ct);
        tracked.Failed($"{error.Code}: {error.Message}", clock.GetUtcNow());
        audit.Add("licensing.sync.failed", "LicensingSync", null, error.Code, success: false);
        await unitOfWork.SaveChangesAsync(ct);
        LogFailed(logger, error.Code, tracked.ConsecutiveFailures);
        _ = state;
        return new SyncOutcome(false, 0, 0, error.Code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Licensing full reconcile: {Tenants} tenants, {Changed} changed")]
    private static partial void LogReconciled(ILogger logger, int tenants, int changed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licensing sync failed with {Code} ({Failures} consecutive failures)")]
    private static partial void LogFailed(ILogger logger, string code, int failures);
}
