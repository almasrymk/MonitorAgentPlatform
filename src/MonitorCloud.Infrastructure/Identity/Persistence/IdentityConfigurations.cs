using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Identity.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", Schemas.Identity);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.FullName).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500);
        builder.Property(x => x.Role).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PreferredLanguage).HasMaxLength(5).IsRequired();
        builder.Property(x => x.InvitationTokenHash).HasMaxLength(128);
        builder.HasIndex(x => x.InvitationTokenHash).IsUnique().HasFilter("[InvitationTokenHash] IS NOT NULL");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property<List<Guid>>("_locationScope")
            .HasField("_locationScope")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("LocationScope")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<Guid>() : JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>(),
                new ValueComparer<List<Guid>>((a, b) => a!.SequenceEqual(b!), v => v.Aggregate(0, (h, g) => HashCode.Combine(h, g)), v => v.ToList()))
            .IsRequired();
        builder.Ignore(x => x.LocationScope);
        builder.HasIndex(x => new { x.TenantId, x.Role, x.Status });
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", Schemas.Identity);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.FamilyId);
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.CreatedIp).HasMaxLength(64);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
