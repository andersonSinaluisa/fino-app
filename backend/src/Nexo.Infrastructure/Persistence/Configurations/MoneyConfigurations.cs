using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
        builder.Property(t => t.AccountMask).HasMaxLength(8);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.SourceConfidence).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(500);

        builder.Ignore(t => t.SignedAmount);
        builder.Ignore(t => t.CountsTowardsBalance);

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
