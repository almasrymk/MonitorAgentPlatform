using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Licensing.Persistence;

internal static class JsonColumns
{
    public static PropertyBuilder<List<T>> AsJsonList<T>(this PropertyBuilder<List<T>> property) =>
        property
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(v, (JsonSerializerOptions?)null) ?? new List<T>(),
                new ValueComparer<List<T>>((a, b) => a!.SequenceEqual(b!), v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)), v => v.ToList()))
            .IsRequired();
}

internal sealed class TenantEntitlementConfiguration : IEntityTypeConfiguration<TenantEntitlement>
{
    public void Configure(EntityTypeBuilder<TenantEntitlement> builder)
    {
        builder.ToTable("TenantEntitlements", Schemas.Licensing);
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.PlanCode).HasMaxLength(32);
        builder.Property(x => x.PlanName).HasMaxLength(200);
        builder.Property(x => x.SubscriptionStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property<List<string>>("_features").HasField("_features").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnName("Features").AsJsonList();
        builder.Property<List<Guid>>("_licenseIds").HasField("_licenseIds").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnName("LicenseIds").AsJsonList();
        builder.Ignore(x => x.Features);
        builder.Ignore(x => x.LicenseIds);
        builder.Property(x => x.SyncError).HasMaxLength(500);
        builder.HasIndex(x => new { x.PlanCode, x.SubscriptionStatus });
        builder.HasIndex(x => x.RenewsAt);
    }
}

internal sealed class DeviceLicenseConfiguration : IEntityTypeConfiguration<DeviceLicense>
{
    public void Configure(EntityTypeBuilder<DeviceLicense> builder)
    {
        builder.ToTable("DeviceLicenses", Schemas.Licensing);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.LicenseNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ReasonCode).HasMaxLength(64);
        builder.Property(x => x.Token).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Kid).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.State });
        builder.HasIndex(x => x.CheckAfter);
    }
}

internal sealed class LicensingSyncStateConfiguration : IEntityTypeConfiguration<LicensingSyncState>
{
    public void Configure(EntityTypeBuilder<LicensingSyncState> builder)
    {
        builder.ToTable("SyncState", Schemas.Licensing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Cursor).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(1000);
    }
}
