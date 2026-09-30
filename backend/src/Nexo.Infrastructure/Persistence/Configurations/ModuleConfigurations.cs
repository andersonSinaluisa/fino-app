using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Budgets;
using Nexo.Domain.Categories;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Imports;
using Nexo.Domain.Insights;
using Nexo.Domain.Notifications;
using Nexo.Domain.Pulses;
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

        // Entregable 14 ("Categorización v2"): merchant/provider/amount-range match
        // criteria added alongside the original description pattern.
        builder.Property(r => r.MerchantPattern).HasMaxLength(120);
        builder.Property(r => r.ProviderCode).HasMaxLength(40);
        builder.Property(r => r.MinAmount).HasPrecision(18, 2);
        builder.Property(r => r.MaxAmount).HasPrecision(18, 2);

        // Computed, not persisted -- same treatment as Transaction.SignedAmount /
        // EffectiveMerchant in MoneyConfigurations.cs.
        builder.Ignore(r => r.Specificity);
        builder.Ignore(r => r.MatchTextLength);

        builder.HasIndex(r => new { r.UserId, r.IsActive, r.Priority });
        builder.HasIndex(r => new { r.UserId, r.MerchantPattern });

        // "Categorización personal": lets CategorizationRuleService look up "does
        // this user already have a rule for this exact pattern" (point 13, conflict
        // detection) without a table scan.
        builder.HasIndex(r => new { r.UserId, r.Pattern });

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
        // Onboarding funcional: se agregó la propiedad y el snapshot a mano
        // (ver 20260909150000_AddImportDetectedProviderCode) pero se olvidó esta
        // línea -- sin ella el modelo que EF calcula en vivo no coincidía con
        // el snapshot congelado y el contenedor no arrancaba (PendingModelChangesWarning).
        builder.Property(i => i.DetectedProviderCode).HasMaxLength(40);
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
        builder.Property(r => r.SuggestedCategorySource).HasConversion<string>().HasMaxLength(16).IsRequired();
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

public sealed class ImportColumnMappingConfiguration : IEntityTypeConfiguration<ImportColumnMapping>
{
    public void Configure(EntityTypeBuilder<ImportColumnMapping> builder)
    {
        builder.ToTable("import_column_mappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ProviderCode).HasMaxLength(40).IsRequired();

        // One saved mapping per institution per user: a re-mapping replaces it
        // (see ImportColumnMapping's doc comment) rather than growing a history.
        builder.HasIndex(m => new { m.UserId, m.ProviderCode }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
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
        // Entregable 16: both readers (GET /insights, GetHomeSummaryAsync) filter
        // out anything past ValidUntil on top of DisplayOrder.
        builder.HasIndex(i => new { i.UserId, i.ValidUntil });

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

/// <summary>PULSO.</summary>
public sealed class FinancialPulseConfiguration : IEntityTypeConfiguration<FinancialPulse>
{
    public void Configure(EntityTypeBuilder<FinancialPulse> builder)
    {
        builder.ToTable("financial_pulses");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(p => p.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(140).IsRequired();
        builder.Property(p => p.Body).HasMaxLength(400).IsRequired();
        builder.Property(p => p.Explanation).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Value).HasPrecision(18, 2);
        builder.Property(p => p.ComparisonValue).HasPrecision(18, 2);
        builder.Property(p => p.PercentChange).HasPrecision(9, 2);
        builder.Property(p => p.ReferenceId).HasMaxLength(64);
        builder.Property(p => p.RelevanceScore).HasPrecision(5, 2);
        builder.Property(p => p.DedupKey).HasMaxLength(160).IsRequired();

        // PulseEngine's every-call dedup/cooldown check is exactly this shape:
        // "does a row with this UserId + DedupKey already exist (optionally
        // within a window)".
        builder.HasIndex(p => new { p.UserId, p.DedupKey });
        // Read side: PulseService.ListAsync orders by relevance within a user.
        builder.HasIndex(p => new { p.UserId, p.RelevanceScore });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Presupuestos. Only the plan is persisted; spent/remaining/reserved/committed are
/// derived from transactions on read, so there is no column that could drift out
/// of sync with the movements.
/// </summary>
public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", table =>
        {
            table.HasCheckConstraint("ck_budgets_amount_positive", "\"Amount\" > 0");
            table.HasCheckConstraint("ck_budgets_end_after_start", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
        });
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(Budget.NameMaxLength).IsRequired();
        builder.Property(b => b.Amount).HasPrecision(18, 2);
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
        builder.Property(b => b.Period).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();

        // The list/overview query: "this user's active budgets".
        builder.HasIndex(b => new { b.UserId, b.IsActive });

        // The overlap rule and "budgets for this category": (user, category, active)
        // plus the date columns the rule compares.
        builder.HasIndex(b => new { b.UserId, b.CategoryId, b.IsActive, b.StartDate, b.EndDate });

        builder.HasIndex(b => b.CategoryId);

        builder.HasMany(b => b.Revisions)
            .WithOne()
            .HasForeignKey(r => r.BudgetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Revisions)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_revisions");

        // A budget is personal financial planning: it goes with the account.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a user's own category deletes the budgets that tracked it rather
        // than silently turning them into a "general" budget with another meaning.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BudgetAmountRevisionConfiguration : IEntityTypeConfiguration<BudgetAmountRevision>
{
    public void Configure(EntityTypeBuilder<BudgetAmountRevision> builder)
    {
        builder.ToTable("budget_amount_revisions", table =>
            table.HasCheckConstraint("ck_budget_amount_revisions_amount_positive", "\"Amount\" > 0"));
        builder.HasKey(r => r.Id);

        // Revisions are created inside the Budget aggregate with their UUID v7 already
        // set and reach the context through the Budget.Revisions navigation, not through
        // DbSet.Add. With EF's default "Guid keys are generated on add", a new revision
        // with a key would be mistaken for an existing row and UPDATEd (0 rows -> 500).
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Amount).HasPrecision(18, 2);

        // One amount per budget per effective date; also the lookup "amount for window".
        builder.HasIndex(r => new { r.BudgetId, r.EffectiveFrom }).IsUnique();
    }
}
