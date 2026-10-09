using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Telemetry.Contracts;
using MonitorCloud.Domain.Telemetry;

namespace MonitorCloud.Application.Telemetry;

internal sealed class TelemetryReportReader(IReadDbContext db) : ITelemetryReportReader
{
    public const int Batch = 1000;

    public async Task<IReadOnlyDictionary<Guid, DeviceUsage>> UsageAsync(IReadOnlyCollection<Guid> deviceIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var start = from.UtcDateTime;
        var end = to.UtcDateTime;
        var result = new Dictionary<Guid, DeviceUsage>();
        foreach (var chunk in deviceIds.Chunk(Batch))
        {
            var rows = await db.Query<MetricHour>()
                .Where(h => chunk.Contains(h.DeviceId) && h.BucketUtc >= start && h.BucketUtc < end)
                .GroupBy(h => h.DeviceId)
                .Select(g => new
                {
                    g.Key,
                    Hours = g.Count(),
                    Samples = g.Sum(h => (decimal)h.Samples),
                    Cpu = g.Sum(h => h.CpuAvg * h.Samples),
                    CpuMax = g.Max(h => h.CpuMax),
                    Ram = g.Sum(h => h.RamAvg * h.Samples),
                    RamMax = g.Max(h => h.RamMax),
                    DiskMax = g.Max(h => h.DiskPercentMax),
                    Rx = g.Sum(h => h.NetRxBytes ?? (h.NetRxBps ?? 0) * 3600),
                    Tx = g.Sum(h => h.NetTxBytes ?? (h.NetTxBps ?? 0) * 3600),
                })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                result[r.Key] = new DeviceUsage(
                    r.Key, r.Hours, r.Samples == 0 ? null : Math.Round(r.Cpu / r.Samples, 2), r.CpuMax, r.Samples == 0 ? null : Math.Round(r.Ram / r.Samples, 2), r.RamMax, r.DiskMax,
                    r.Rx, r.Tx);
            }
        }

        return result;
    }
}
