using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Audit;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(u => u.PreferredCurrency).HasMaxLength(3).IsRequired();
        builder.Property(u => u.Locale).HasMaxLength(16).IsRequired();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(64);
        builder.Property(t => t.DeviceLabel).HasMaxLength(120);
        builder.Property(t => t.CreatedFromIpHash).HasMaxLength(128);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(80).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.ResourceId).HasMaxLength(64);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpHash).HasMaxLength(128);
        builder.Property(a => a.UserAgent).HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(300);

        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
        builder.HasIndex(a => a.Action);
    }
}
