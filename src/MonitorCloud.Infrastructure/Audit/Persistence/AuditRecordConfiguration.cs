using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Audit.Persistence;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords", Schemas.Audit);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(AuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(AuditRecord.EntityTypeMaxLength).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(AuditRecord.EntityIdMaxLength);
        builder.Property(x => x.Details).HasMaxLength(AuditRecord.DetailsMaxLength);
        builder.Property(x => x.Ip).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.At).HasPrecision(3);
        builder.HasIndex(x => new { x.TenantId, x.At }).IsDescending(false, true).HasDatabaseName("IX_AuditRecords_Tenant_At");
    }
}
