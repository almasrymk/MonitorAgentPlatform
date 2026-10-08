using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Configuration.Persistence;

internal sealed class DeviceConfigurationConfiguration : IEntityTypeConfiguration<DeviceConfiguration>
{
    public void Configure(EntityTypeBuilder<DeviceConfiguration> builder)
    {
        builder.ToTable("DeviceConfigurations", Schemas.Configuration);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.DocumentJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class DeviceConfigurationAckConfiguration : IEntityTypeConfiguration<DeviceConfigurationAck>
{
    public void Configure(EntityTypeBuilder<DeviceConfigurationAck> builder)
    {
        builder.ToTable("DeviceConfigurationAcks", Schemas.Configuration);
        builder.HasKey(x => x.DeviceId);
        builder.Property(x => x.DeviceId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class TenantConfigurationDefaultsConfiguration : IEntityTypeConfiguration<TenantConfigurationDefaults>
{
    public void Configure(EntityTypeBuilder<TenantConfigurationDefaults> builder)
    {
        builder.ToTable("TenantConfigurationDefaults", Schemas.Configuration);
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.DocumentJson).HasColumnType("nvarchar(max)").IsRequired();
    }
}
