using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Devices.Persistence;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices", Schemas.Devices);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(Device.NameMaxLength).IsRequired();
        builder.Property(x => x.Hostname).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Fingerprint }).IsUnique();
        builder.Property(x => x.OsName).HasMaxLength(200);
        builder.Property(x => x.OsVersion).HasMaxLength(100);
        builder.Property(x => x.Architecture).HasMaxLength(32);
        builder.Property(x => x.AgentVersion).HasMaxLength(32);
        builder.Property(x => x.LocalIp).HasMaxLength(64);
        builder.Property(x => x.PublicIp).HasMaxLength(64);
        builder.Property(x => x.MacAddress).HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.LocationId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}

internal sealed class DeviceCredentialConfiguration : IEntityTypeConfiguration<DeviceCredential>
{
    public void Configure(EntityTypeBuilder<DeviceCredential> builder)
    {
        builder.ToTable("DeviceCredentials", Schemas.Devices);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        builder.HasOne<Device>().WithOne().HasForeignKey<DeviceCredential>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeviceStateConfiguration : IEntityTypeConfiguration<DeviceState>
{
    public void Configure(EntityTypeBuilder<DeviceState> builder)
    {
        builder.ToTable("DeviceStates", Schemas.Devices);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.CpuPercent).HasPrecision(5, 2);
        builder.Property(x => x.RamPercent).HasPrecision(5, 2);
        builder.Property(x => x.DiskPercent).HasPrecision(5, 2);
        builder.Property(x => x.LastSeenAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.LastTelemetryAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.ConnectedSince).HasColumnType("datetime2(3)");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");
        builder.HasOne<Device>().WithOne().HasForeignKey<DeviceState>(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);

        // Every list and dashboard groups by these (02 section 4).
        builder.HasIndex(x => new { x.TenantId, x.LocationId, x.Health });
        builder.HasIndex(x => new { x.TenantId, x.Connection });
        builder.HasIndex(x => new { x.LicenseState, x.UnlicensedSince });
    }
}

internal sealed class InventoryDocumentConfiguration : IEntityTypeConfiguration<InventoryDocument>
{
    public void Configure(EntityTypeBuilder<InventoryDocument> builder)
    {
        builder.ToTable("InventoryDocuments", Schemas.Devices);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Json).HasColumnType("varbinary(max)");
        builder.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.DeviceId, x.Kind }).IsUnique();
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
