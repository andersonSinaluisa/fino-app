using Nexo.Domain.Common;

namespace Nexo.Domain.CreditCards;

/// <summary>Where the official figures of a statement came from.</summary>
public enum StatementSource
{
    /// <summary>Read from an imported statement file.</summary>
    Imported = 0,

    /// <summary>Typed by the person from their bank's statement/app.</summary>
    Manual = 1,
}

/// <summary>
/// The lifecycle of a statement. Never stored: derived by
/// <see cref="CreditCardCalculator"/> from the payments that actually exist, so an
/// edited or deleted payment can never leave a stale "Pagado" behind.
/// </summary>
public enum StatementStatus
{
    /// <summary>The current cycle: still accumulating movements.</summary>
    Open = 0,

    /// <summary>Closed, nothing paid yet, not past due.</summary>
    Closed = 1,

    /// <summary>Closed, partly paid, not past due.</summary>
    PartiallyPaid = 2,

    /// <summary>Fully paid (or nothing to pay).</summary>
    Paid = 3,

    /// <summary>Past the payment date and not fully paid.</summary>
    Overdue = 4,
}

/// <summary>
/// Estados de cuenta de la tarjeta: ONLY the official figures the bank printed (or
/// the person typed) for one closing -- total a pagar, pago mínimo and fecha máxima.
/// Everything else about a statement (its movements, how much was paid, what is
/// still pending, its status) is derived from the stored movements every time it is
/// read, so it can never drift from them.
///
/// <para>When a cycle has no declared statement, <see cref="CreditCardCalculator"/>
/// computes its balance from the movements; when it has one, the bank's total wins.</para>
/// </summary>
public sealed class CreditCardStatement : Entity, IUserOwned
{
    private CreditCardStatement()
    {
    }

    public Guid UserId { get; private set; }

    public Guid CreditCardId { get; private set; }

    public DateOnly? PeriodStart { get; private set; }

    public DateOnly ClosingDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    /// <summary>"Total a pagar" of this statement.</summary>
    public decimal StatementBalance { get; private set; }

    public decimal? MinimumPayment { get; private set; }

    public StatementSource Source { get; private set; }

    /// <summary>The import the figures were read from, for traceability.</summary>
    public Guid? ImportId { get; private set; }

    public static CreditCardStatement Declare(
        CreditCard card,
        DateOnly closingDate,
        DateOnly dueDate,
        decimal statementBalance,
        decimal? minimumPayment,
        StatementSource source,
        DateTimeOffset now,
        DateOnly? periodStart = null,
        Guid? importId = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        var statement = new CreditCardStatement
        {
            UserId = card.UserId,
            CreditCardId = card.Id,
        };
        statement.Revise(closingDate, dueDate, statementBalance, minimumPayment, source, now, periodStart, importId);
        return statement;
    }

    public void Revise(
        DateOnly closingDate,
        DateOnly dueDate,
        decimal statementBalance,
        decimal? minimumPayment,
        StatementSource source,
        DateTimeOffset now,
        DateOnly? periodStart = null,
        Guid? importId = null)
    {
        if (dueDate <= closingDate)
        {
            throw new DomainException("statement_due_before_closing", "La fecha máxima de pago debe ser posterior a la fecha de corte.");
        }

        if (periodStart is { } start && start > closingDate)
        {
            throw new DomainException("statement_invalid_period", "El inicio del periodo no puede ser posterior al corte.");
        }

        var balance = MoneyMath.Round(statementBalance);
        if (balance < 0m)
        {
            throw new DomainException("statement_invalid_balance", "El total a pagar no puede ser negativo.");
        }

        if (minimumPayment is { } minimum)
        {
            var roundedMinimum = MoneyMath.Round(minimum);
            if (roundedMinimum < 0m || roundedMinimum > balance)
            {
                throw new DomainException("statement_invalid_minimum", "El pago mínimo debe estar entre cero y el total a pagar.");
            }

            MinimumPayment = roundedMinimum;
        }
        else
        {
            MinimumPayment = null;
        }

        PeriodStart = periodStart;
        ClosingDate = closingDate;
        DueDate = dueDate;
        StatementBalance = balance;
        Source = source;
        ImportId = importId ?? (source == StatementSource.Imported ? ImportId : null);
        Stamp(now);
    }

    public DeclaredStatement ToDeclared() => new(ClosingDate, DueDate, StatementBalance, MinimumPayment);
}

/// <summary>The official figures of one statement, as the calculator consumes them.</summary>
public sealed record DeclaredStatement(DateOnly ClosingDate, DateOnly DueDate, decimal Balance, decimal? MinimumPayment);
