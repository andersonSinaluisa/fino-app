using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Categories;
using Nexo.Domain.Accounts;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Persistence.Configurations;

public sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("providers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ShortName).HasMaxLength(60).IsRequired();
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(p => p.SupportedModesRaw).HasMaxLength(200).IsRequired();
        builder.Property(p => p.StatementParserCode).HasMaxLength(40);
        builder.Property(p => p.BrandColor).HasMaxLength(9).IsRequired();
        builder.Property(p => p.LogoKey).HasMaxLength(60);

        builder.Ignore(p => p.SupportedModes);
        builder.Ignore(p => p.DefaultMode);
        builder.Ignore(p => p.HasAutomaticConnection);

        builder.HasIndex(p => p.Code).IsUnique();
    }
}

public sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ProviderCode).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Alias).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Mask).HasMaxLength(8);
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.AccountType).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(a => a.ConnectionMode).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(a => a.LastVerifiedBalance).HasPrecision(18, 2);
        builder.Property(a => a.EstimatedBalance).HasPrecision(18, 2);

        builder.Ignore(a => a.BalanceKind);

        builder.HasIndex(a => new { a.UserId, a.IsArchived });
        builder.HasIndex(a => a.ProviderCode);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ProviderCode).HasMaxLength(40).IsRequired();
        builder.Property(t => t.ExternalReference).HasMaxLength(64);
        builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(400).IsRequired();
        builder.Property(t => t.NormalizedDescription).HasMaxLength(400).IsRequired();
        builder.Property(t => t.Merchant).HasMaxLength(120);
        builder.Property(t => t.MerchantCorrected).HasMaxLength(120);
        builder.Property(t => t.AccountMask).HasMaxLength(8);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.SourceConfidence).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(500);

        // "Categorización personal": why CategoryId is what it is. No FK on
        // CategorizationRuleId, same reasoning as PossibleDuplicateOfId/
        // InternalTransferLinkId below -- deleting a rule must never cascade into
        // (or be blocked by) historical movements (point 17, "eliminar una regla no
        // debe modificar transacciones históricas").
        builder.Property(t => t.CategorySource).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Ignore(t => t.SignedAmount);
        builder.Ignore(t => t.CountsTowardsBalance);
        builder.Ignore(t => t.EffectiveMerchant);

        // Entregable 13: no FK, same as PossibleDuplicateOfId above -- just a plain
        // nullable Guid pointing at another row in this same table, resolved by the
        // application layer. A real FK here would fight the per-user query filter
        // the same way ids.Contains(...) does (see ADR-009 / QueryableGuidExtensions).
        builder.Property(t => t.IsInternalTransfer).IsRequired();

        // Movimientos divididos. SplitVersion is an optimistic-concurrency token:
        // two simultaneous "guardar división" on the same movement cannot both
        // succeed (the second UPDATE matches 0 rows and is rejected with 409).
        builder.Property(t => t.IsSplit).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.SplitVersion).IsRequired().HasDefaultValue(0).IsConcurrencyToken();

        builder.HasMany(t => t.Splits)
            .WithOne()
            .HasForeignKey(s => s.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Splits)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_splits");

        // The movements feed: user + date is the ordering used by every list query.
        builder.HasIndex(t => new { t.UserId, t.TransactionDate });
        builder.HasIndex(t => new { t.FinancialAccountId, t.TransactionDate });

        // Deduplication lookups. Not unique on purpose: two genuine identical
        // purchases on the same day share a fingerprint, and the matcher — not the
        // database — decides what that means.
        builder.HasIndex(t => new { t.FinancialAccountId, t.Fingerprint });
        builder.HasIndex(t => new { t.FinancialAccountId, t.ExternalReference });
        builder.HasIndex(t => new { t.UserId, t.CategoryId });
        builder.HasIndex(t => t.ProviderCode);
        builder.HasIndex(t => t.ImportId);

        // "Categorización personal", point 20 ("performance"): the impact-preview
        // and recategorize-historical-matches queries always start from `UserId`
        // (never scan other users' movements) and then filter on the normalized
        // description -- this index keeps that first step an index range scan
        // instead of a sequential scan over the whole table.
        builder.HasIndex(t => new { t.UserId, t.NormalizedDescription });

        // Registro rápido de efectivo (§36): idempotencia del guardado rápido. El
        // índice es único y parcial -- solo cubre las filas que de verdad traen un
        // ClientRequestId (los movimientos manuales), así que los millones de
        // movimientos importados, todos con NULL, no entran en él. Es la garantía
        // real contra el doble toque: aunque dos peticiones lleguen a la vez y
        // ambas pasen la comprobación previa, la base de datos deja pasar solo una.
        builder.HasIndex(t => new { t.UserId, t.ClientRequestId })
            .IsUnique()
            .HasFilter("\"ClientRequestId\" IS NOT NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(t => t.FinancialAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Movimientos divididos: the analytic distribution of one movement. Deleting the
/// movement deletes its splits (cascade). Deleting a category that a split still
/// uses is refused by the database (NO ACTION) instead of silently rewriting or
/// destroying historical financial data.
/// </summary>
public sealed class TransactionSplitConfiguration : IEntityTypeConfiguration<TransactionSplit>
{
    public void Configure(EntityTypeBuilder<TransactionSplit> builder)
    {
        builder.ToTable("transaction_splits", table =>
            table.HasCheckConstraint("ck_transaction_splits_amount_positive", "\"Amount\" > 0"));
        builder.HasKey(s => s.Id);

        // Splits are created inside the Transaction aggregate with their UUID v7
        // already set and reach the context through the navigation; see the same
        // note on BudgetAmountRevision.
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.Note).HasMaxLength(TransactionSplit.NoteMaxLength);

        builder.HasIndex(s => s.TransactionId);
        builder.HasIndex(s => s.CategoryId);
        builder.HasIndex(s => new { s.TransactionId, s.CategoryId });
        builder.HasIndex(s => new { s.UserId, s.CategoryId });

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
