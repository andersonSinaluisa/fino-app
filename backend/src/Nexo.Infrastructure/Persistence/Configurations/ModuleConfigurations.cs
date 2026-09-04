using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Categories;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Imports;
using Nexo.Domain.Insights;
using Nexo.Domain.Notifications;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(60).IsRequired();
        builder.Property(c => c.Icon).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Color).HasMaxLength(9).IsRequired();

        builder.HasIndex(c => new { c.UserId, c.Code }).IsUnique();

        // System categories have a null UserId and no owner; a user's own
        // categories go with the account.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CategorizationRuleConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    public void Configure(EntityTypeBuilder<CategorizationRule> builder)
    {
        builder.ToTable("categorization_rules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Pattern).HasMaxLength(120).IsRequired();
        builder.Property(r => r.MatchKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(r => new { r.UserId, r.IsActive, r.Priority });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Rules learnt from a user's corrections carry merchant names taken from
        // their own movements, so they must disappear with the account.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CategoryCorrectionConfiguration : IEntityTypeConfiguration<CategoryCorrection>
{
    public void Configure(EntityTypeBuilder<CategoryCorrection> builder)
    {
        builder.ToTable("category_corrections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.NormalizedDescription).HasMaxLength(400).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImportConfiguration : IEntityTypeConfiguration<Import>
{
    public void Configure(EntityTypeBuilder<Import> builder)
    {
        builder.ToTable("imports");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.FileName).HasMaxLength(260).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(i => i.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.ParserCode).HasMaxLength(40);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(i => i.FailureReason).HasMaxLength(500);
        builder.Property(i => i.IncomeTotal).HasPrecision(18, 2);
        builder.Property(i => i.ExpenseTotal).HasPrecision(18, 2);
        builder.Property(i => i.DeclaredClosingBalance).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.UserId, i.CreatedAt });
        builder.HasIndex(i => new { i.FinancialAccountId, i.ContentHash });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.ToTable("import_rows");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(r => r.MatchType).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Description).HasMaxLength(400);
        builder.Property(r => r.ExternalReference).HasMaxLength(64);
        builder.Property(r => r.Fingerprint).HasMaxLength(64);
        builder.Property(r => r.Error).HasMaxLength(400);
        builder.Property(r => r.RawPayload).HasMaxLength(2000);

        builder.HasIndex(r => new { r.ImportId, r.RowNumber });

        builder.HasOne<Import>()
            .WithMany()
            .HasForeignKey(r => r.ImportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmailConnectionConfiguration : IEntityTypeConfiguration<EmailConnection>
{
    public void Configure(EntityTypeBuilder<EmailConnection> builder)
    {
        builder.ToTable("email_connections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ProviderKind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(c => c.EmailAddress).HasMaxLength(320).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.SecretReference).HasMaxLength(200);
        builder.Property(c => c.GrantedScopes).HasMaxLength(400);
        builder.Property(c => c.SyncCursor).HasMaxLength(200);
        builder.Property(c => c.InboundAddress).HasMaxLength(120);

        builder.HasIndex(c => new { c.UserId, c.Status });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TrustedSenderConfiguration : IEntityTypeConfiguration<TrustedSender>
{
    public void Configure(EntityTypeBuilder<TrustedSender> builder)
    {
        builder.ToTable("trusted_senders");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.ProviderCode).HasMaxLength(40).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(200).IsRequired();

        builder.HasIndex(s => new { s.ProviderCode, s.Value }).IsUnique();
    }
}

public sealed class InsightConfiguration : IEntityTypeConfiguration<Insight>
{
    public void Configure(EntityTypeBuilder<Insight> builder)
    {
        builder.ToTable("insights");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Code).HasMaxLength(60).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(140).IsRequired();
        builder.Property(i => i.Body).HasMaxLength(400).IsRequired();
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(i => i.ReferenceId).HasMaxLength(64);
        builder.Property(i => i.Value).HasPrecision(18, 2);
        builder.Property(i => i.ComparisonValue).HasPrecision(18, 2);
        builder.Property(i => i.PercentChange).HasPrecision(9, 2);

        builder.HasIndex(i => new { i.UserId, i.DisplayOrder });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ExpoPushToken).HasMaxLength(255).IsRequired();
        builder.Property(d => d.Platform).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.DeviceName).HasMaxLength(120);
        builder.Property(d => d.AppVersion).HasMaxLength(32);

        builder.HasIndex(d => new { d.UserId, d.ExpoPushToken }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(120).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(300).IsRequired();
        builder.Property(n => n.Payload).HasMaxLength(1000);

        builder.HasIndex(n => new { n.UserId, n.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
