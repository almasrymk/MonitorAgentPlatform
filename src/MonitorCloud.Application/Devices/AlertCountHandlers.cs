using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices.Agent;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Monitoring;

namespace MonitorCloud.Application.Devices;

/// <summary>Open alert counts and health of <c>DeviceStates</c> follow the device's alerts (02 section 12).</summary>
internal sealed class ApplyOpenAlertCounts(IAppDbContext db, IUnitOfWork unitOfWork, IOpenAlertCounter counter, ILicensingPolicy policy, ILiveNotifier live, TimeProvider clock)
    : IIntegrationEventHandler<AlertRaisedV1>, IIntegrationEventHandler<AlertSeverityChangedV1>, IIntegrationEventHandler<AlertResolvedV1>
{
    public Task HandleAsync(AlertRaisedV1 integrationEvent, CancellationToken cancellationToken) => ApplyAsync(integrationEvent.DeviceId, cancellationToken);

    public Task HandleAsync(AlertSeverityChangedV1 integrationEvent, CancellationToken cancellationToken) => ApplyAsync(integrationEvent.DeviceId, cancellationToken);

    public Task HandleAsync(AlertResolvedV1 integrationEvent, CancellationToken cancellationToken) => ApplyAsync(integrationEvent.DeviceId, cancellationToken);

    private async Task ApplyAsync(Guid deviceId, CancellationToken ct)
    {
        var state = await db.Set<DeviceState>().SingleOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (state is null)
            return;
        var counts = await counter.CountAsync(deviceId, ct);
        if (counts.Critical == state.OpenCritical && counts.Warning == state.OpenWarning)
            return;
        state.SetOpenAlerts(counts.Critical, counts.Warning, clock.GetUtcNow(), policy.UnlicensedGrace);
        await unitOfWork.SaveChangesAsync(ct);
        await live.DeviceStateChangedAsync([DeviceLive.Change(state)], ct);
    }
}

internal sealed class DeviceNames(IReadDbContext db) : Contracts.IDeviceNames
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct) =>
        deviceIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Query<Device>().Where(d => deviceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
}
