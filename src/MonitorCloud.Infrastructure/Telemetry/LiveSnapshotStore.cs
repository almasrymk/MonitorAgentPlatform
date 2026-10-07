using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using MonitorCloud.Application.Telemetry.Contracts;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Telemetry;

/// <summary>Latest snapshot per device in memory; persisted to <c>telemetry.LiveSnapshots</c> at most once a minute.</summary>
internal sealed class LiveSnapshotStore(DatabaseOptionsAccessor database, TimeProvider clock) : ILiveSnapshotStore
{
    public static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, (StoredSnapshot Snapshot, DateTimeOffset PersistedAt)> _latest = new();

    public async Task SaveAsync(Guid deviceId, Guid tenantId, byte[] json, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var snapshot = new StoredSnapshot(json, capturedAt);
        var persist = !_latest.TryGetValue(deviceId, out var current) || now - current.PersistedAt >= PersistInterval;
        _latest[deviceId] = (snapshot, persist ? now : current.PersistedAt);
        if (!persist)
            return;
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.ExecuteAsync("""
            MERGE telemetry.LiveSnapshots AS t
            USING (SELECT d.Id AS DeviceId, d.TenantId FROM devices.Devices d WHERE d.Id = @deviceId AND d.TenantId = @tenantId) AS s ON t.DeviceId = s.DeviceId
            WHEN MATCHED THEN UPDATE SET Json = @json, CapturedAt = @capturedAt
            WHEN NOT MATCHED THEN INSERT (DeviceId, TenantId, Json, CapturedAt) VALUES (s.DeviceId, s.TenantId, @json, @capturedAt);
            """, new { deviceId, tenantId, json, capturedAt });
    }

    public async Task<StoredSnapshot?> GetAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (_latest.TryGetValue(deviceId, out var current))
            return current.Snapshot;
        await using var connection = new SqlConnection(database.ConnectionString);
        var row = await connection.QuerySingleOrDefaultAsync<(byte[] Json, DateTimeOffset CapturedAt)?>(
            "SELECT Json, CapturedAt FROM telemetry.LiveSnapshots WHERE DeviceId = @deviceId", new { deviceId });
        return row is { } r ? new StoredSnapshot(r.Json, r.CapturedAt) : null;
    }
}

/// <summary>The application's view of <see cref="Devices.InventoryCodec"/>.</summary>
internal sealed class InventoryCodecAdapter : Application.Abstractions.Serialization.IInventoryCodec
{
    public string Decode(byte[] compressed) => Devices.InventoryCodec.Decode(compressed);
}
