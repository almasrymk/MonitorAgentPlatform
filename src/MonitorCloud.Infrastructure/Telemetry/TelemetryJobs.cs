using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Telemetry;

/// <summary>
/// <c>MetricRollupJob</c> every 5 minutes: completed hours of the last day into <c>MetricHours</c> (weighted average,
/// max of maxes, P95 = max of minute P95s), idempotent <c>MERGE</c>. <c>TelemetryRetention</c> once a day: deletes in
/// batches of 50,000 (02 section 5).
/// </summary>
public sealed partial class TelemetryJobs(DatabaseOptionsAccessor database, IOptions<TelemetryOptions> options, TimeProvider clock, ILogger<TelemetryJobs> logger) : BackgroundService
{
    public static readonly TimeSpan RollupInterval = TimeSpan.FromMinutes(5);
    public const int DeleteBatch = 50_000;

    private DateTimeOffset _lastRetention = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.JobsEnabled)
            return;
        using var timer = new PeriodicTimer(RollupInterval, clock);
        do
        {
            try
            {
                await RollupAsync(stoppingToken);
                if (clock.GetUtcNow() - _lastRetention > TimeSpan.FromDays(1))
                {
                    await RetentionAsync(stoppingToken);
                    _lastRetention = clock.GetUtcNow();
                }
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Rolls up the completed hours since <paramref name="since"/> (default: the last 26 hours). Returns the rows merged.</summary>
    public async Task<int> RollupAsync(CancellationToken cancellationToken, DateTime? since = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteAsync("""
            MERGE telemetry.MetricHours AS t
            USING (
                SELECT DeviceId, MIN(TenantId) AS TenantId, DATEADD(hour, DATEDIFF(hour, 0, BucketUtc), 0) AS BucketUtc,
                    CAST(SUM(Samples) AS smallint) AS Samples,
                    CAST(SUM(CpuAvg * Samples) / NULLIF(SUM(Samples), 0) AS decimal(5,2)) AS CpuAvg, MAX(CpuMax) AS CpuMax, MAX(CpuP95) AS CpuP95,
                    CAST(SUM(RamAvg * Samples) / NULLIF(SUM(Samples), 0) AS decimal(5,2)) AS RamAvg, MAX(RamMax) AS RamMax,
                    CAST(AVG(DiskActiveAvg) AS decimal(5,2)) AS DiskActiveAvg, CAST(AVG(DiskReadBps) AS bigint) AS DiskReadBps, CAST(AVG(DiskWriteBps) AS bigint) AS DiskWriteBps,
                    CAST(AVG(DiskResponseMs) AS decimal(9,2)) AS DiskResponseMs, CAST(AVG(NetRxBps) AS bigint) AS NetRxBps, CAST(AVG(NetTxBps) AS bigint) AS NetTxBps,
                    SUM(NetRxBytes) AS NetRxBytes, SUM(NetTxBytes) AS NetTxBytes, CAST(AVG(PingMs) AS decimal(9,2)) AS PingMs,
                    CAST(AVG(PacketLossPercent) AS decimal(5,2)) AS PacketLossPercent, MAX(TempMaxC) AS TempMaxC, MAX(DiskPercentMax) AS DiskPercentMax
                FROM telemetry.MetricMinutes
                WHERE BucketUtc >= @since AND BucketUtc < @currentHour
                GROUP BY DeviceId, DATEADD(hour, DATEDIFF(hour, 0, BucketUtc), 0)
            ) AS s ON t.DeviceId = s.DeviceId AND t.BucketUtc = s.BucketUtc
            WHEN MATCHED THEN UPDATE SET Samples = s.Samples, CpuAvg = ISNULL(s.CpuAvg, 0), CpuMax = s.CpuMax, CpuP95 = s.CpuP95, RamAvg = ISNULL(s.RamAvg, 0), RamMax = s.RamMax,
                DiskActiveAvg = s.DiskActiveAvg, DiskReadBps = s.DiskReadBps, DiskWriteBps = s.DiskWriteBps, DiskResponseMs = s.DiskResponseMs, NetRxBps = s.NetRxBps,
                NetTxBps = s.NetTxBps, NetRxBytes = s.NetRxBytes, NetTxBytes = s.NetTxBytes, PingMs = s.PingMs, PacketLossPercent = s.PacketLossPercent, TempMaxC = s.TempMaxC,
                DiskPercentMax = s.DiskPercentMax
            WHEN NOT MATCHED THEN INSERT (DeviceId, TenantId, BucketUtc, Samples, CpuAvg, CpuMax, CpuP95, RamAvg, RamMax, DiskActiveAvg, DiskReadBps, DiskWriteBps, DiskResponseMs,
                NetRxBps, NetTxBps, NetRxBytes, NetTxBytes, PingMs, PacketLossPercent, TempMaxC, DiskPercentMax)
                VALUES (s.DeviceId, s.TenantId, s.BucketUtc, s.Samples, ISNULL(s.CpuAvg, 0), s.CpuMax, s.CpuP95, ISNULL(s.RamAvg, 0), s.RamMax, s.DiskActiveAvg, s.DiskReadBps,
                s.DiskWriteBps, s.DiskResponseMs, s.NetRxBps, s.NetTxBps, s.NetRxBytes, s.NetTxBytes, s.PingMs, s.PacketLossPercent, s.TempMaxC, s.DiskPercentMax);
            """, new { since = since ?? currentHour.AddHours(-26), currentHour }, commandTimeout: 300);
    }

    /// <summary>
    /// Deletes rows older than the retention windows: Platform Settings when saved, otherwise the <c>Telemetry</c> options.
    /// Returns the rows deleted.
    /// </summary>
    public async Task<int> RetentionAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var o = options.Value;
        var total = 0;
        await using var connection = new SqlConnection(database.ConnectionString);
        var platform = await connection.QuerySingleOrDefaultAsync<(int Minute, int Hour)?>(
            "SELECT MinuteRetentionDays AS Minute, HourRetentionDays AS Hour FROM tenancy.PlatformSettings");
        var minuteDays = platform?.Minute ?? o.RetentionMinuteDays;
        var hourDays = platform?.Hour ?? o.RetentionHourDays;
        var diskDays = platform?.Hour ?? o.RetentionDiskDays;
        foreach (var (table, days) in new[] { ("telemetry.MetricMinutes", minuteDays), ("telemetry.MetricHours", hourDays), ("telemetry.DiskUsageHours", diskDays), ("telemetry.MonitorPointSamples", minuteDays) })
        {
            int deleted;
            do
            {
                // Table names are constants of this class, never input.
                deleted = await connection.ExecuteAsync($"DELETE TOP ({DeleteBatch}) FROM {table} WHERE BucketUtc < @cutoff", new { cutoff = now.AddDays(-days) }, commandTimeout: 300);
                total += deleted;
            }
            while (deleted == DeleteBatch && !cancellationToken.IsCancellationRequested);
        }

        return total;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Telemetry rollup or retention failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
