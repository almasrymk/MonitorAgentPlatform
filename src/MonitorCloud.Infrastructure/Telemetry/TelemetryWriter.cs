using System.Data;
using System.Threading.Channels;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Telemetry.Contracts;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Telemetry;

/// <summary>
/// Drains the ingestion channel every 2 s or 1,000 rows (05 section 3): bulk-copies the minutes into a staging table,
/// inserts the new ones idempotently, keeps the hourly disk sizes, updates <c>DeviceStates</c> in one statement, and
/// only then completes the batches so the gateway acknowledges them. A failed write is retried with back-off; the
/// agents keep their data meanwhile.
/// </summary>
public sealed partial class TelemetryWriter : BackgroundService, ITelemetryIngest
{
    private readonly Channel<TelemetryBatch> _channel;
    private readonly DatabaseOptionsAccessor _database;
    private readonly ILiveNotifier _live;
    private readonly TelemetryOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<TelemetryWriter> _logger;
    private readonly ManualResetEventSlim _running = new(true);

    public TelemetryWriter(DatabaseOptionsAccessor database, ILiveNotifier live, IOptions<TelemetryOptions> options, TimeProvider clock, ILogger<TelemetryWriter> logger)
    {
        _database = database;
        _live = live;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        _channel = Channel.CreateBounded<TelemetryBatch>(new BoundedChannelOptions(Math.Max(1, _options.ChannelCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    /// <summary>Test hook: the next writes fail as if the database were down.</summary>
    public bool SimulateDatabaseFailure { get; set; }

    /// <summary>Batches waiting to be written.</summary>
    public int Pending => _channel.Reader.Count;

    /// <summary>Test hook: stops writing (the channel fills up and readers wait).</summary>
    public void Pause() => _running.Reset();

    public void Resume() => _running.Set();

    public ValueTask EnqueueAsync(TelemetryBatch batch, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(batch, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batches = new List<TelemetryBatch>();
        while (await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            await Task.Run(() => _running.Wait(stoppingToken), stoppingToken);
            batches.Clear();
            var rows = 0;
            var deadline = _clock.GetUtcNow().AddSeconds(_options.FlushSeconds);
            while (rows < _options.FlushRows)
            {
                if (_channel.Reader.TryRead(out var batch))
                {
                    batches.Add(batch);
                    rows += Math.Max(1, batch.Minutes.Count);
                    continue;
                }

                var wait = deadline - _clock.GetUtcNow();
                if (wait <= TimeSpan.Zero || batches.Count == 0)
                    break;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(wait);
                try
                {
                    if (!await _channel.Reader.WaitToReadAsync(timeout.Token))
                        break;
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (batches.Count > 0)
                await WriteWithRetryAsync([.. batches], stoppingToken);
        }
    }

    /// <summary>Writes everything currently queued; for tests and shutdown.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batches = new List<TelemetryBatch>();
        while (_channel.Reader.TryRead(out var batch))
            batches.Add(batch);
        if (batches.Count > 0)
            await WriteWithRetryAsync(batches, cancellationToken);
    }

    private async Task WriteWithRetryAsync(IReadOnlyList<TelemetryBatch> batches, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                var changed = await WriteAsync(batches, cancellationToken);
                foreach (var batch in batches)
                    batch.Committed.TrySetResult();
                await NotifyAsync(changed, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException && !cancellationToken.IsCancellationRequested)
            {
                LogWriteFailed(_logger, ex, batches.Count);
                // Real time on purpose: the retry must happen even when tests freeze the clock.
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
            }
        }
    }

    private async Task<IReadOnlyList<Guid>> WriteAsync(IReadOnlyList<TelemetryBatch> batches, CancellationToken cancellationToken)
    {
        if (SimulateDatabaseFailure)
            throw new InvalidOperationException("Simulated database failure.");

        var now = _clock.GetUtcNow();
        var oldest = now.UtcDateTime.AddDays(-_options.RetentionMinuteDays);
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(Sql.CreateStaging, transaction: transaction);
        await BulkCopyAsync(connection, transaction, "#Minutes", MinuteTable(batches, oldest), cancellationToken);
        await BulkCopyAsync(connection, transaction, "#Disks", DiskTable(batches, now), cancellationToken);
        await BulkCopyAsync(connection, transaction, "#States", StateTable(batches), cancellationToken);
        await connection.ExecuteAsync(Sql.Merge, transaction: transaction, commandTimeout: 60);
        await transaction.CommitAsync(cancellationToken);
        return batches.Select(b => b.DeviceId).Distinct().ToList();
    }

    private static async Task BulkCopyAsync(SqlConnection connection, SqlTransaction transaction, string table, DataTable data, CancellationToken cancellationToken)
    {
        if (data.Rows.Count == 0)
            return;
        using var copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = table, BatchSize = 5000 };
        foreach (DataColumn column in data.Columns)
            copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await copy.WriteToServerAsync(data, cancellationToken);
    }

    private async Task NotifyAsync(IReadOnlyList<Guid> deviceIds, CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;
        try
        {
            await using var connection = new SqlConnection(_database.ConnectionString);
            var rows = await connection.QueryAsync<StateRow>(Sql.States, new { ids = deviceIds });
            var changes = rows.Select(r => new DeviceStateChange(
                r.DeviceId, r.Tenant, r.LocationId, r.Connection == 1 ? "Online" : "Offline", ((Domain.Devices.DeviceHealth)r.Health).ToString(),
                r.LicenseState == 1 ? "Licensed" : "Unlicensed", r.CpuPercent, r.RamPercent, r.DiskPercent,
                r.LastSeenAt is { } seen ? new DateTimeOffset(DateTime.SpecifyKind(seen, DateTimeKind.Utc)) : null)).ToList();
            await _live.DeviceStateChangedAsync(changes, cancellationToken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // The data is committed; a missed live hint is corrected by the next refresh.
            LogNotifyFailed(_logger, ex);
        }
    }

    private static DataTable MinuteTable(IReadOnlyList<TelemetryBatch> batches, DateTime oldest)
    {
        var table = new DataTable();
        foreach (var (name, type) in Columns.Minutes)
            table.Columns.Add(name, type);
        // One row per (device, minute) across all batches of the flush: a resent minute must not break the insert.
        var seen = new HashSet<(Guid, DateTime)>();
        foreach (var batch in batches)
        {
            // Minutes beyond the retention window are acknowledged and dropped (05 section 2, rule 6).
            foreach (var m in batch.Minutes.Where(m => m.BucketUtc >= oldest))
            {
                if (!seen.Add((batch.DeviceId, m.BucketUtc)))
                    continue;
                table.Rows.Add(batch.DeviceId, batch.TenantId, m.BucketUtc, m.Samples, m.CpuAvg, m.CpuMax, m.CpuP95, m.RamAvg, m.RamMax, Db(m.DiskActiveAvg), Db(m.DiskReadBps),
                    Db(m.DiskWriteBps), Db(m.DiskResponseMs), Db(m.NetRxBps), Db(m.NetTxBps), Db(m.NetRxBytes), Db(m.NetTxBytes), Db(m.PingMs), Db(m.PacketLossPercent),
                    Db(m.TempMaxC), m.DiskPercentMax);
            }
        }

        return table;
    }

    private static DataTable DiskTable(IReadOnlyList<TelemetryBatch> batches, DateTimeOffset now)
    {
        var table = new DataTable();
        foreach (var (name, type) in Columns.Disks)
            table.Columns.Add(name, type);
        // The newest size per (device, drive, hour) across the flush (MERGE needs unique source rows).
        var newest = new Dictionary<(Guid, string, DateTime), (TelemetryBatch Batch, DiskRow Disk)>();
        foreach (var batch in batches)
        {
            var hour = batch.ReceivedAt.UtcDateTime;
            hour = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, DateTimeKind.Utc);
            foreach (var d in batch.Disks)
                newest[(batch.DeviceId, Truncate(d.Drive, 16)!.ToUpperInvariant(), hour)] = (batch, d);
        }

        foreach (var ((_, _, hour), (batch, d)) in newest)
        {
            table.Rows.Add(batch.DeviceId, batch.TenantId, Truncate(d.Drive, 16), hour, Db(Truncate(d.Label, 100)), Db(Truncate(d.FileSystem, 16)), d.TotalGb, d.UsedGb, d.FreeGb);
        }

        _ = now;
        return table;
    }

    private static DataTable StateTable(IReadOnlyList<TelemetryBatch> batches)
    {
        var table = new DataTable();
        foreach (var (name, type) in Columns.States)
            table.Columns.Add(name, type);
        foreach (var device in batches.GroupBy(b => b.DeviceId))
        {
            var newest = device.SelectMany(b => b.Minutes).OrderByDescending(m => m.BucketUtc).FirstOrDefault();
            var last = device.Last();
            table.Rows.Add(device.Key, last.TenantId, newest is null ? DBNull.Value : newest.CpuAvg, newest is null ? DBNull.Value : newest.RamAvg,
                newest is null ? DBNull.Value : newest.DiskPercentMax, newest is null ? DBNull.Value : newest.UptimeSeconds,
                newest is null ? DBNull.Value : newest.BucketUtc.AddMinutes(1), device.Max(b => b.Sequence));
        }

        return table;
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

    // Tenant, not TenantId: a row read for the notification, not a tenant-owned entity.
    private sealed record StateRow(Guid DeviceId, Guid Tenant, Guid LocationId, byte Connection, byte Health, byte LicenseState, decimal? CpuPercent, decimal? RamPercent, decimal? DiskPercent, DateTime? LastSeenAt);

    private static class Columns
    {
        public static readonly (string, Type)[] Minutes =
        [
            ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("BucketUtc", typeof(DateTime)), ("Samples", typeof(short)), ("CpuAvg", typeof(decimal)), ("CpuMax", typeof(decimal)),
            ("CpuP95", typeof(decimal)), ("RamAvg", typeof(decimal)), ("RamMax", typeof(decimal)), ("DiskActiveAvg", typeof(decimal)), ("DiskReadBps", typeof(long)),
            ("DiskWriteBps", typeof(long)), ("DiskResponseMs", typeof(decimal)), ("NetRxBps", typeof(long)), ("NetTxBps", typeof(long)), ("NetRxBytes", typeof(long)),
            ("NetTxBytes", typeof(long)), ("PingMs", typeof(decimal)), ("PacketLossPercent", typeof(decimal)), ("TempMaxC", typeof(decimal)), ("DiskPercentMax", typeof(decimal)),
        ];

        public static readonly (string, Type)[] Disks =
        [
            ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("Drive", typeof(string)), ("BucketUtc", typeof(DateTime)), ("Label", typeof(string)), ("FileSystem", typeof(string)),
            ("TotalGb", typeof(decimal)), ("UsedGb", typeof(decimal)), ("FreeGb", typeof(decimal)),
        ];

        public static readonly (string, Type)[] States =
        [
            ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("CpuPercent", typeof(decimal)), ("RamPercent", typeof(decimal)), ("DiskPercent", typeof(decimal)),
            ("UptimeSeconds", typeof(long)), ("TelemetryAt", typeof(DateTime)), ("Sequence", typeof(long)),
        ];
    }

    private static class Sql
    {
        public const string CreateStaging = """
            CREATE TABLE #Minutes (DeviceId uniqueidentifier NOT NULL, TenantId uniqueidentifier NOT NULL, BucketUtc datetime2(0) NOT NULL, Samples smallint NOT NULL,
                CpuAvg decimal(5,2) NOT NULL, CpuMax decimal(5,2) NOT NULL, CpuP95 decimal(5,2) NOT NULL, RamAvg decimal(5,2) NOT NULL, RamMax decimal(5,2) NOT NULL,
                DiskActiveAvg decimal(5,2) NULL, DiskReadBps bigint NULL, DiskWriteBps bigint NULL, DiskResponseMs decimal(9,2) NULL, NetRxBps bigint NULL, NetTxBps bigint NULL,
                NetRxBytes bigint NULL, NetTxBytes bigint NULL, PingMs decimal(9,2) NULL, PacketLossPercent decimal(5,2) NULL, TempMaxC decimal(5,1) NULL, DiskPercentMax decimal(5,2) NOT NULL);
            CREATE TABLE #Disks (DeviceId uniqueidentifier NOT NULL, TenantId uniqueidentifier NOT NULL, Drive nvarchar(16) NOT NULL, BucketUtc datetime2(0) NOT NULL,
                Label nvarchar(100) NULL, FileSystem nvarchar(16) NULL, TotalGb decimal(12,2) NOT NULL, UsedGb decimal(12,2) NOT NULL, FreeGb decimal(12,2) NOT NULL);
            CREATE TABLE #States (DeviceId uniqueidentifier NOT NULL, TenantId uniqueidentifier NOT NULL, CpuPercent decimal(5,2) NULL, RamPercent decimal(5,2) NULL,
                DiskPercent decimal(5,2) NULL, UptimeSeconds bigint NULL, TelemetryAt datetime2(0) NULL, Sequence bigint NOT NULL);
            """;

        // Idempotent on the primary keys; the tenant of every row comes from the device token (checked against the device).
        public const string Merge = """
            INSERT INTO telemetry.MetricMinutes (DeviceId, TenantId, BucketUtc, Samples, CpuAvg, CpuMax, CpuP95, RamAvg, RamMax, DiskActiveAvg, DiskReadBps, DiskWriteBps,
                DiskResponseMs, NetRxBps, NetTxBps, NetRxBytes, NetTxBytes, PingMs, PacketLossPercent, TempMaxC, DiskPercentMax)
            SELECT m.DeviceId, m.TenantId, m.BucketUtc, m.Samples, m.CpuAvg, m.CpuMax, m.CpuP95, m.RamAvg, m.RamMax, m.DiskActiveAvg, m.DiskReadBps, m.DiskWriteBps,
                m.DiskResponseMs, m.NetRxBps, m.NetTxBps, m.NetRxBytes, m.NetTxBytes, m.PingMs, m.PacketLossPercent, m.TempMaxC, m.DiskPercentMax
            FROM #Minutes m
            JOIN devices.Devices d ON d.Id = m.DeviceId AND d.TenantId = m.TenantId
            WHERE NOT EXISTS (SELECT 1 FROM telemetry.MetricMinutes x WHERE x.DeviceId = m.DeviceId AND x.BucketUtc = m.BucketUtc);

            MERGE telemetry.DiskUsageHours AS t
            USING (SELECT s.* FROM #Disks s JOIN devices.Devices d ON d.Id = s.DeviceId AND d.TenantId = s.TenantId) AS s
            ON t.DeviceId = s.DeviceId AND t.Drive = s.Drive AND t.BucketUtc = s.BucketUtc
            WHEN MATCHED THEN UPDATE SET Label = s.Label, FileSystem = s.FileSystem, TotalGb = s.TotalGb, UsedGb = s.UsedGb, FreeGb = s.FreeGb
            WHEN NOT MATCHED THEN INSERT (DeviceId, TenantId, Drive, BucketUtc, Label, FileSystem, TotalGb, UsedGb, FreeGb)
                VALUES (s.DeviceId, s.TenantId, s.Drive, s.BucketUtc, s.Label, s.FileSystem, s.TotalGb, s.UsedGb, s.FreeGb);

            UPDATE ds SET
                CpuPercent = COALESCE(s.CpuPercent, ds.CpuPercent),
                RamPercent = COALESCE(s.RamPercent, ds.RamPercent),
                DiskPercent = COALESCE(s.DiskPercent, ds.DiskPercent),
                UptimeSeconds = COALESCE(s.UptimeSeconds, ds.UptimeSeconds),
                LastTelemetryAt = CASE WHEN s.TelemetryAt IS NULL OR (ds.LastTelemetryAt IS NOT NULL AND ds.LastTelemetryAt > s.TelemetryAt) THEN ds.LastTelemetryAt ELSE s.TelemetryAt END,
                LastEventSequence = CASE WHEN s.Sequence > ds.LastEventSequence THEN s.Sequence ELSE ds.LastEventSequence END,
                UpdatedAt = SYSUTCDATETIME()
            FROM devices.DeviceStates ds
            JOIN #States s ON s.DeviceId = ds.DeviceId AND s.TenantId = ds.TenantId;
            """;

        public const string States = """
            SELECT DeviceId, TenantId AS Tenant, LocationId, Connection, Health, LicenseState, CpuPercent, RamPercent, DiskPercent, LastSeenAt
            FROM devices.DeviceStates WHERE DeviceId IN @ids
            """;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telemetry write of {Batches} batches failed; retrying")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, int batches);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live notification after a telemetry write failed")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception);

    public override void Dispose()
    {
        _running.Dispose();
        base.Dispose();
    }
}
