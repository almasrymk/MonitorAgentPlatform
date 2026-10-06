using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Messaging.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", Schemas.Messaging);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.OccurredAt).HasPrecision(3);
        builder.Property(x => x.ProcessedAt).HasPrecision(3);
        builder.Property(x => x.NextAttemptAt).HasPrecision(3);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => new { x.NextAttemptAt, x.OccurredAt })
            .HasFilter("[ProcessedAt] IS NULL AND [DeadLettered] = 0")
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages", Schemas.Messaging);
        builder.HasKey(x => new { x.MessageId, x.Handler });
        builder.Property(x => x.Handler).HasMaxLength(200);
        builder.Property(x => x.ProcessedAt).HasPrecision(3);
    }
}
