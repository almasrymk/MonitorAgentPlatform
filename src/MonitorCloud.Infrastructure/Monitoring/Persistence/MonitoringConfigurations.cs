using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Monitoring.Persistence;

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts", Schemas.Monitoring);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.IssueKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.Title).HasMaxLength(Alert.TitleMax).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(Alert.MessageMax).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.ResolvedBy).HasConversion<string>().HasMaxLength(8);

        // At most one open alert per condition (02 section 6).
        builder.HasIndex(x => new { x.DeviceId, x.IssueKey }).IsUnique().HasFilter("[Status] = 'Open'");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.Severity, x.LastSeenAt }).IsDescending(false, false, false, true);
        builder.HasIndex(x => new { x.TenantId, x.LocationId, x.FirstSeenAt }).IsDescending(false, false, true);
        builder.HasIndex(x => new { x.DeviceId, x.Status });
    }
}

internal sealed class AlertDailyStatConfiguration : IEntityTypeConfiguration<AlertDailyStat>
{
    public void Configure(EntityTypeBuilder<AlertDailyStat> builder)
    {
        builder.ToTable("AlertDailyStats", Schemas.Monitoring);
        builder.HasKey(x => new { x.TenantId, x.LocationId, x.Day });
        builder.Ignore(x => x.Id);
    }
}

internal sealed class MonitorPointConfiguration : IEntityTypeConfiguration<MonitorPoint>
{
    public void Configure(EntityTypeBuilder<MonitorPoint> builder)
    {
        builder.ToTable("MonitorPoints", Schemas.Monitoring);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Target).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AlertLevel).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Origin).HasMaxLength(8).IsRequired();
        builder.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.DeviceId, x.Key }).IsUnique();
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class MonitorPointStateConfiguration : IEntityTypeConfiguration<MonitorPointState>
{
    public void Configure(EntityTypeBuilder<MonitorPointState> builder)
    {
        builder.ToTable("MonitorPointStates", Schemas.Monitoring);
        builder.HasKey(x => x.MonitorPointId);
        builder.Property(x => x.MonitorPointId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.Message).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ResponseMs).HasPrecision(9, 2);
        builder.HasOne<MonitorPoint>().WithOne().HasForeignKey<MonitorPointState>(x => x.MonitorPointId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.DeviceId);
    }
}

internal sealed class MonitoringSettingsConfiguration : IEntityTypeConfiguration<MonitoringSettings>
{
    public void Configure(EntityTypeBuilder<MonitoringSettings> builder)
    {
        builder.ToTable("MonitoringSettings", Schemas.Monitoring);
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.OfflineSeverity).HasConversion<string>().HasMaxLength(8);
    }
}

internal sealed class PendingOfflineAlertConfiguration : IEntityTypeConfiguration<PendingOfflineAlert>
{
    public void Configure(EntityTypeBuilder<PendingOfflineAlert> builder)
    {
        builder.ToTable("PendingOfflineAlerts", Schemas.Monitoring);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.WentOfflineAt);
    }
}
