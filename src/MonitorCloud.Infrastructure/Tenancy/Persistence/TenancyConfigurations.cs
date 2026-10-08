using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Tenancy.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", Schemas.Tenancy);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(Tenant.CodeMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => x.LicensingCustomerId).IsUnique().HasFilter("[LicensingCustomerId] IS NOT NULL");
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.DefaultLanguage).HasMaxLength(2).HasDefaultValue("en");
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.SuspensionReason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.Status, x.Name });
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations", Schemas.Tenancy);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsDefault }).IsUnique().HasFilter("[IsDefault] = 1").HasDatabaseName("UX_Locations_Tenant_Default");
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.AddressLine).HasMaxLength(300);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.ContactName).HasMaxLength(200);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LocationEnrollmentCodeConfiguration : IEntityTypeConfiguration<LocationEnrollmentCode>
{
    public void Configure(EntityTypeBuilder<LocationEnrollmentCode> builder)
    {
        builder.ToTable("LocationEnrollmentCodes", Schemas.Tenancy);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.Property(x => x.CodePrefix).HasMaxLength(8).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.LocationId });
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
    }
}