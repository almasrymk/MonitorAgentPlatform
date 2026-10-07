using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Serialization;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Telemetry.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Telemetry;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Telemetry;

public static class TelemetryErrors
{
    public static readonly Error DeviceNotFound = Error.NotFound("DEVICE_NOT_FOUND", "Device not found.");
}

public sealed record MetricPointDto(DateTimeOffset At, decimal? Avg, decimal? Max);

public sealed record MetricSeriesDto(string Metric, IReadOnlyList<MetricPointDto> Points);

public sealed record DeviceMetricsDto(string Resolution, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<MetricSeriesDto> Series);

/// <summary>
/// <c>GET /devices/{id}/metrics?from&amp;to&amp;metrics</c> (06): minutes up to 6 hours, otherwise hours; at most 1,500
/// points per series (thinned evenly).
/// </summary>
[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceMetricsQuery(Guid DeviceId, DateTimeOffset? From, DateTimeOffset? To, string? Metrics) : IQuery<DeviceMetricsDto>;

internal sealed class GetDeviceMetricsQueryValidator : AbstractValidator<GetDeviceMetricsQuery>
{
    public GetDeviceMetricsQueryValidator()
    {
        RuleFor(x => x).Must(q => q.From is null || q.To is null || q.From < q.To).WithMessage("from must be before to.");
        RuleFor(x => x).Must(q => q.From is null || q.To is null || q.To.Value - q.From.Value <= TimeSpan.FromDays(400)).WithMessage("The range may be at most 400 days.");
        RuleFor(x => x.Metrics).Must(m => m is null || m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(GetDeviceMetricsQueryHandler.Known.Contains))
            .WithMessage("metrics may contain cpu, ram, disk, network, ping, temp.");
    }
}

internal sealed class GetDeviceMetricsQueryHandler(IReadDbContext db, IDeviceDirectory devices, TimeProvider clock) : IQueryHandler<GetDeviceMetricsQuery, DeviceMetricsDto>
{
    public static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase) { "cpu", "ram", "disk", "network", "ping", "temp" };
    public const int MaxPoints = 1500;

    public async Task<Result<DeviceMetricsDto>> Handle(GetDeviceMetricsQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return TelemetryErrors.DeviceNotFound;
        var to = request.To ?? clock.GetUtcNow();
        var from = request.From ?? to.AddHours(-24);
        var minutes = to - from <= TimeSpan.FromHours(6);
        var metrics = (request.Metrics ?? "cpu,ram,disk,network").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m.ToLowerInvariant()).Distinct().ToList();
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;

        IQueryable<MetricBucket> rows = minutes
            ? db.Query<MetricMinute>().Where(m => m.DeviceId == request.DeviceId && m.BucketUtc >= fromUtc && m.BucketUtc < toUtc)
            : db.Query<MetricHour>().Where(m => m.DeviceId == request.DeviceId && m.BucketUtc >= fromUtc && m.BucketUtc < toUtc);
        var data = await rows.OrderBy(m => m.BucketUtc)
            .Select(m => new Row(m.BucketUtc, m.CpuAvg, m.CpuMax, m.RamAvg, m.RamMax, m.DiskPercentMax, m.DiskActiveAvg, m.NetRxBps, m.NetTxBps, m.PingMs, m.TempMaxC))
            .ToListAsync(cancellationToken);
        var thinned = Thin(data);

        var series = metrics.Select(metric => new MetricSeriesDto(metric, thinned.Select(r => metric switch
        {
            "cpu" => Point(r.At, r.CpuAvg, r.CpuMax),
            "ram" => Point(r.At, r.RamAvg, r.RamMax),
            "disk" => Point(r.At, r.DiskPercent, r.DiskActive),
            "network" => Point(r.At, r.RxBps, r.TxBps),
            "ping" => Point(r.At, r.PingMs, null),
            _ => Point(r.At, r.TempMaxC, null),
        }).ToList())).ToList();
        return new DeviceMetricsDto(minutes ? "minute" : "hour", from, to, series);
    }

    private static MetricPointDto Point(DateTime at, decimal? avg, decimal? max) => new(new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)), avg, max);

    private static List<Row> Thin(List<Row> rows)
    {
        if (rows.Count <= MaxPoints)
            return rows;
        var step = (double)rows.Count / MaxPoints;
        return Enumerable.Range(0, MaxPoints).Select(i => rows[(int)(i * step)]).ToList();
    }

    private sealed record Row(DateTime At, decimal CpuAvg, decimal CpuMax, decimal RamAvg, decimal RamMax, decimal DiskPercent, decimal? DiskActive, long? Rx, long? Tx, decimal? PingMs, decimal? TempMaxC)
    {
        public decimal? RxBps => Rx;
        public decimal? TxBps => Tx;
    }
}

public sealed record DiskDto(string Drive, string? Label, string? FileSystem, decimal TotalGb, decimal UsedGb, decimal FreeGb, decimal UsagePercent, string Status, DateTimeOffset At);

/// <summary><c>GET /devices/{id}/disks</c>: the latest size of each partition (Warning from 85%, Critical from 92%).</summary>
[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceDisksQuery(Guid DeviceId) : IQuery<IReadOnlyList<DiskDto>>;

internal sealed class GetDeviceDisksQueryHandler(IReadDbContext db, IDeviceDirectory devices) : IQueryHandler<GetDeviceDisksQuery, IReadOnlyList<DiskDto>>
{
    public const decimal WarningPercent = 85;
    public const decimal CriticalPercent = 92;

    public async Task<Result<IReadOnlyList<DiskDto>>> Handle(GetDeviceDisksQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return TelemetryErrors.DeviceNotFound;
        var latest = await db.Query<DiskUsageHour>()
            .Where(d => d.DeviceId == request.DeviceId)
            .GroupBy(d => d.Drive)
            .Select(g => g.OrderByDescending(d => d.BucketUtc).First())
            .ToListAsync(cancellationToken);
        IReadOnlyList<DiskDto> disks = latest.OrderBy(d => d.Drive, StringComparer.OrdinalIgnoreCase).Select(d =>
        {
            var usage = d.TotalGb == 0 ? 0 : Math.Round(100 * d.UsedGb / d.TotalGb, 1);
            var status = usage >= CriticalPercent ? "Critical" : usage >= WarningPercent ? "Warning" : "Healthy";
            return new DiskDto(d.Drive, d.Label, d.FileSystem, d.TotalGb, d.UsedGb, d.FreeGb, usage, status, new DateTimeOffset(DateTime.SpecifyKind(d.BucketUtc, DateTimeKind.Utc)));
        }).ToList();
        return Result.Success(disks);
    }
}

public sealed record DeviceOverviewDto(DateTimeOffset? CapturedAt, JsonElement? Snapshot, IReadOnlyList<object> MonitorPoints, IReadOnlyList<object> Messages);

/// <summary>
/// <c>GET /devices/{id}/overview</c>: the latest snapshot (05 section 5); monitor point shortcuts and messages arrive
/// with the Monitoring module.
/// </summary>
[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceOverviewQuery(Guid DeviceId) : IQuery<DeviceOverviewDto>;

internal sealed class GetDeviceOverviewQueryHandler(IDeviceDirectory devices, ILiveSnapshotStore snapshots, IInventoryCodec codec)
    : IQueryHandler<GetDeviceOverviewQuery, DeviceOverviewDto>
{
    public async Task<Result<DeviceOverviewDto>> Handle(GetDeviceOverviewQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return TelemetryErrors.DeviceNotFound;
        var snapshot = await snapshots.GetAsync(request.DeviceId, cancellationToken);
        JsonElement? json = null;
        if (snapshot is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(codec.Decode(snapshot.Json));
                json = document.RootElement.Clone();
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
            {
                json = null;
            }
        }

        return new DeviceOverviewDto(snapshot?.CapturedAt, json, [], []);
    }
}
