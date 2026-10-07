using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Telemetry.Contracts;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary><c>Snapshot</c> (best effort, at most one a minute) -> <see cref="ILiveSnapshotStore"/> and <c>snapshotUpdated</c>.</summary>
public sealed class SnapshotHandler(ILiveSnapshotStore store, ILiveNotifier live, TimeProvider clock) : IAgentMessageHandler
{
    public const int MaxBytes = 1024 * 1024;

    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.Snapshot;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Snapshot.JsonBrotli.ToByteArray();
        if (session.Restricted || bytes.Length == 0 || bytes.Length > MaxBytes)
            return;
        var at = clock.GetUtcNow();
        await store.SaveAsync(session.DeviceId, session.TenantId, bytes, at, cancellationToken);
        await live.SnapshotUpdatedAsync(session.DeviceId, at, cancellationToken);
    }
}

/// <summary><c>InventoryUpdate</c> (guaranteed) -> <see cref="UpsertInventoryCommand"/>, acknowledged after the save.</summary>
public sealed class InventoryHandler(IServiceScopeFactory scopes, TimeProvider clock) : IAgentMessageHandler
{
    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.Inventory;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        var update = message.Inventory;
        if (!session.Restricted && update.Kind != AgentProtocol.V1.InventoryKind.Unspecified && !string.IsNullOrWhiteSpace(update.Hash))
        {
            var kind = (Domain.Devices.InventoryKind)((int)update.Kind - 1);
            await using var scope = scopes.CreateAsyncScope();
            var saved = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new UpsertInventoryCommand(session.DeviceId, session.TenantId, kind, update.Hash, update.JsonBrotli.ToByteArray()), cancellationToken);
            if (saved.IsFailure && saved.Error!.Kind != SharedKernel.ErrorKind.Validation)
                return;
        }

        await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
    }
}

/// <summary><c>LiveSample</c> (live mode only) -> SignalR group <c>device:{id}</c>, never stored (05 section 4).</summary>
public sealed class LiveSampleHandler(ILiveNotifier live, TimeProvider clock) : IAgentMessageHandler
{
    /// <summary>Samples closer together than this are dropped (the agent sends every 2 s).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1500);

    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.LiveSample;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.GetUtcNow();
        if (session.Restricted || !session.TryTakeLiveSlot(now, MinInterval))
            return;
        var s = message.LiveSample;
        await live.LiveSampleAsync(new LiveSampleChange(session.DeviceId, now, Round(s.CpuPercent), Round(s.RamPercent), Round(s.DiskActivePercent),
            (long)Math.Min(s.NetRxBps, long.MaxValue), (long)Math.Min(s.NetTxBps, long.MaxValue), s.HasCpuTempC ? Round(s.CpuTempC) : null), cancellationToken);
    }

    private static decimal Round(double value) => (decimal)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1000), 1);
}
