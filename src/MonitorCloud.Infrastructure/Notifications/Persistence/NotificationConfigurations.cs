using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Notifications.Persistence;

internal sealed class AlertChannelSettingsConfiguration : IEntityTypeConfiguration<AlertChannelSettings>
{
    public void Configure(EntityTypeBuilder<AlertChannelSettings> builder)
    {
        builder.ToTable("AlertChannelSettings", Schemas.Notifications);
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.TenantId).ValueGeneratedNever();
        builder.Ignore(x => x.Id);
        builder.Property(x => x.WebhookUrl).HasMaxLength(500);
    }
}

internal sealed class AlertRecipientConfiguration : IEntityTypeConfiguration<AlertRecipient>
{
    public void Configure(EntityTypeBuilder<AlertRecipient> builder)
    {
        builder.ToTable("AlertRecipients", Schemas.Notifications);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Events).HasConversion<string>().HasMaxLength(24);
        builder.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", Schemas.Notifications);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Severity).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt }).IsDescending(false, true);
    }
}

internal sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.ToTable("NotificationReads", Schemas.Notifications);
        builder.HasKey(x => new { x.NotificationId, x.UserId });
        builder.Ignore(x => x.Id);
        builder.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.UserId);
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries", Schemas.Notifications);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Channel).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Recipient).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.TenantId, x.AlertId });
    }
}
