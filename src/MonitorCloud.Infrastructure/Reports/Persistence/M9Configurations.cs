using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Archive;
using MonitorCloud.Domain.Media;
using MonitorCloud.Domain.Reports;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Reports.Persistence;

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("MediaFiles", Schemas.Media);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class GeneratedReportConfiguration : IEntityTypeConfiguration<GeneratedReport>
{
    public void Configure(EntityTypeBuilder<GeneratedReport> builder)
    {
        builder.ToTable("GeneratedReports", Schemas.Reports);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ParametersJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Format).HasConversion<string>().HasMaxLength(4);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.RequestedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.Status, x.RequestedAt });
    }
}

internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("CustomerProfiles", Schemas.Archive);
        // One profile per tenant: the id is the tenant id.
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.Property(x => x.Industry).HasMaxLength(100);
        builder.Property(x => x.Website).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.Property(x => x.AccountManager).HasMaxLength(200);
    }
}

internal sealed class ArchiveContactConfiguration : IEntityTypeConfiguration<ArchiveContact>
{
    public void Configure(EntityTypeBuilder<ArchiveContact> builder)
    {
        builder.ToTable("Contacts", Schemas.Archive);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.JobTitle).HasMaxLength(100);
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class ArchiveNoteConfiguration : IEntityTypeConfiguration<ArchiveNote>
{
    public void Configure(EntityTypeBuilder<ArchiveNote> builder)
    {
        builder.ToTable("Notes", Schemas.Archive);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}

internal sealed class ArchiveFileConfiguration : IEntityTypeConfiguration<ArchiveFile>
{
    public void Configure(EntityTypeBuilder<ArchiveFile> builder)
    {
        builder.ToTable("Files", Schemas.Archive);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.UploadedAt });
    }
}

internal sealed class RemoteAccessEntryConfiguration : IEntityTypeConfiguration<RemoteAccessEntry>
{
    public void Configure(EntityTypeBuilder<RemoteAccessEntry> builder)
    {
        builder.ToTable("RemoteAccessEntries", Schemas.Archive);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Tool).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IdentifierProtected).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.PasswordProtected).HasMaxLength(4000);
        builder.HasIndex(x => x.TenantId);
    }
}

internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("PlatformSettings", Schemas.Tenancy);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.BrandName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EmailSenderName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EmailSenderAddress).HasMaxLength(256).IsRequired();
    }
}
