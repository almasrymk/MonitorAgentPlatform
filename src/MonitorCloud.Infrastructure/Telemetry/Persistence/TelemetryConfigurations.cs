using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Telemetry;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Telemetry.Persistence;

internal static class MetricColumns
{
    public static void Configure<T>(EntityTypeBuilder<T> builder, string table)
        where T : MetricBucket
    {
        builder.ToTable(table, Schemas.Telemetry);
        builder.HasKey(x => new { x.DeviceId, x.BucketUtc });
        builder.Ignore(x => x.Id);
        builder.Property(x => x.BucketUtc).HasColumnType("datetime2(0)");
        foreach (var name in new[] { nameof(MetricBucket.CpuAvg), nameof(MetricBucket.CpuMax), nameof(MetricBucket.CpuP95), nameof(MetricBucket.RamAvg), nameof(MetricBucket.RamMax),
                     nameof(MetricBucket.DiskActiveAvg), nameof(MetricBucket.PacketLossPercent), nameof(MetricBucket.DiskPercentMax) })
            builder.Property(name).HasPrecision(5, 2);
        builder.Property(x => x.DiskResponseMs).HasPrecision(9, 2);
        builder.Property(x => x.PingMs).HasPrecision(9, 2);
        builder.Property(x => x.TempMaxC).HasPrecision(5, 1);
        builder.HasIndex(x => new { x.TenantId, x.BucketUtc });
    }
}

internal sealed class MetricMinuteConfiguration : IEntityTypeConfiguration<MetricMinute>
{
    public void Configure(EntityTypeBuilder<MetricMinute> builder) => MetricColumns.Configure(builder, "MetricMinutes");
}

internal sealed class MetricHourConfiguration : IEntityTypeConfiguration<MetricHour>
{
    public void Configure(EntityTypeBuilder<MetricHour> builder) => MetricColumns.Configure(builder, "MetricHours");
}

internal sealed class DiskUsageHourConfiguration : IEntityTypeConfiguration<DiskUsageHour>
{
    public void Configure(EntityTypeBuilder<DiskUsageHour> builder)
    {
        builder.ToTable("DiskUsageHours", Schemas.Telemetry);
        builder.HasKey(x => new { x.DeviceId, x.Drive, x.BucketUtc });
        builder.Ignore(x => x.Id);
        builder.Property(x => x.Drive).HasMaxLength(16);
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.FileSystem).HasMaxLength(16);
        builder.Property(x => x.BucketUtc).HasColumnType("datetime2(0)");
        builder.Property(x => x.TotalGb).HasPrecision(12, 2);
        builder.Property(x => x.UsedGb).HasPrecision(12, 2);
        builder.Property(x => x.FreeGb).HasPrecision(12, 2);
        builder.HasIndex(x => new { x.TenantId, x.BucketUtc });
    }
}

internal sealed class LiveSnapshotConfiguration : IEntityTypeConfiguration<LiveSnapshot>
{
    public void Configure(EntityTypeBuilder<LiveSnapshot> builder)
    {
        builder.ToTable("LiveSnapshots", Schemas.Telemetry);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.Json).HasColumnType("varbinary(max)");
    }
}

internal sealed class MonitorPointSampleConfiguration : IEntityTypeConfiguration<MonitorPointSample>
{
    public void Configure(EntityTypeBuilder<MonitorPointSample> builder)
    {
        builder.ToTable("MonitorPointSamples", Schemas.Telemetry);
        builder.HasKey(x => new { x.MonitorPointId, x.BucketUtc });
        builder.Ignore(x => x.Id);
        builder.Property(x => x.BucketUtc).HasColumnType("datetime2(0)");
        builder.Property(x => x.ResponseMs).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.DeviceId, x.BucketUtc });
    }
}
