using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexo.Domain.Accounts;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Transactions;

namespace Nexo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tarjetas de crédito: the card's terms, one row per card account. Goes away with
/// the account (cascade); archiving the account keeps it.
/// </summary>
public sealed class CreditCardConfiguration : IEntityTypeConfiguration<CreditCard>
{
    public void Configure(EntityTypeBuilder<CreditCard> builder)
    {
        builder.ToTable("credit_cards", table =>
        {
            table.HasCheckConstraint("ck_credit_cards_limit_positive", "\"CreditLimit\" > 0");
            table.HasCheckConstraint("ck_credit_cards_closing_day", "\"ClosingDay\" BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_credit_cards_due_day", "\"PaymentDueDay\" BETWEEN 1 AND 31");
        });
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Network).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(c => c.CreditLimit).HasPrecision(18, 2).IsRequired();
        builder.Ignore(c => c.Terms);

        builder.HasIndex(c => c.FinancialAccountId).IsUnique();
        builder.HasIndex(c => c.UserId);

        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(c => c.FinancialAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Only the official figures of a statement; everything else is derived.</summary>
public sealed class CreditCardStatementConfiguration : IEntityTypeConfiguration<CreditCardStatement>
{
    public void Configure(EntityTypeBuilder<CreditCardStatement> builder)
    {
        builder.ToTable("credit_card_statements", table =>
        {
            table.HasCheckConstraint("ck_credit_card_statements_balance", "\"StatementBalance\" >= 0");
            table.HasCheckConstraint("ck_credit_card_statements_due_after_closing", "\"DueDate\" > \"ClosingDate\"");
        });
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StatementBalance).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.MinimumPayment).HasPrecision(18, 2);
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(16).IsRequired();

        // One declared statement per closing date and card.
        builder.HasIndex(s => new { s.CreditCardId, s.ClosingDate }).IsUnique();
        builder.HasIndex(s => s.UserId);

        builder.HasOne<CreditCard>()
            .WithMany()
            .HasForeignKey(s => s.CreditCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Compras diferidas. One plan per purchase (unique TransactionId). Deleting the
/// purchase deletes its plan: a schedule for money that was never spent would keep
/// deferring debt that does not exist.
/// </summary>
public sealed class InstallmentPlanConfiguration : IEntityTypeConfiguration<InstallmentPlan>
{
    public void Configure(EntityTypeBuilder<InstallmentPlan> builder)
    {
        builder.ToTable("installment_plans", table =>
        {
            table.HasCheckConstraint("ck_installment_plans_amount_positive", "\"OriginalAmount\" > 0");
            table.HasCheckConstraint("ck_installment_plans_count", "\"NumberOfInstallments\" BETWEEN 2 AND 72");
        });
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OriginalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.InstallmentAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.InterestRate).HasPrecision(5, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(p => p.TransactionId).IsUnique();
        builder.HasIndex(p => new { p.CreditCardId, p.Status });
        builder.HasIndex(p => p.UserId);

        builder.HasMany(p => p.Installments)
            .WithOne()
            .HasForeignKey(i => i.InstallmentPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Installments)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_installments");

        builder.HasOne<CreditCard>()
            .WithMany()
            .HasForeignKey(p => p.CreditCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(p => p.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InstallmentConfiguration : IEntityTypeConfiguration<Installment>
{
    public void Configure(EntityTypeBuilder<Installment> builder)
    {
        builder.ToTable("installments", table =>
            table.HasCheckConstraint("ck_installments_amount_positive", "\"Amount\" > 0"));
        builder.HasKey(i => i.Id);

        // Created inside the plan aggregate with their UUID v7 already set (see the
        // same note on TransactionSplit / BudgetAmountRevision).
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Amount).HasPrecision(18, 2).IsRequired();

        builder.HasIndex(i => new { i.InstallmentPlanId, i.Number }).IsUnique();
        builder.HasIndex(i => new { i.UserId, i.ClosingDate });
    }
}
