using Nexo.Domain.Common;

namespace Nexo.Domain.Audit;

/// <summary>
/// Append-only record of security-relevant actions: sign-in, token revocation,
/// email connection changes, exports and deletions. Deliberately free of amounts,
/// descriptions and any other financial content.
/// </summary>
public sealed class AuditLogEntry : Entity
{
    private AuditLogEntry()
    {
    }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; } = null!;

    public string ResourceType { get; private set; } = null!;

    public string? ResourceId { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>Hashed, never the raw address.</summary>
    public string? IpHash { get; private set; }

    public string? UserAgent { get; private set; }

    public bool Succeeded { get; private set; } = true;

    public string? Detail { get; private set; }

    public static AuditLogEntry Record(
        Guid? userId,
        string action,
        string resourceType,
        DateTimeOffset now,
        string? resourceId = null,
        string? correlationId = null,
        string? ipHash = null,
        string? userAgent = null,
        bool succeeded = true,
        string? detail = null)
    {
        var entry = new AuditLogEntry
        {
            UserId = userId,
            Action = DomainException.RequireText(action, nameof(action), 80),
            ResourceType = DomainException.RequireText(resourceType, nameof(resourceType), 60),
            ResourceId = resourceId,
            CorrelationId = correlationId,
            IpHash = ipHash,
            UserAgent = userAgent?.Length > 200 ? userAgent[..200] : userAgent,
            Succeeded = succeeded,
            Detail = detail?.Length > 300 ? detail[..300] : detail,
        };
        entry.Stamp(now);
        return entry;
    }
}

/// <summary>Canonical action names so queries and alerts do not rely on free text.</summary>
public static class AuditActions
{
    public const string UserRegistered = "user.registered";
    public const string LegalDocumentsAccepted = "legal.documents_accepted";
    public const string ConsentChanged = "legal.consent_changed";
    public const string UserLoggedIn = "user.logged_in";
    public const string UserLoginFailed = "user.login_failed";

    /// <summary>Entregable 20: the failed attempt that crossed the lockout threshold.</summary>
    public const string AccountLocked = "user.account_locked";

    /// <summary>Entregable 20: a login attempt rejected purely because the account is mid-lockout.</summary>
    public const string UserLoginBlocked = "user.login_blocked";

    public const string TokenRefreshed = "auth.token_refreshed";
    public const string TokenReuseDetected = "auth.token_reuse_detected";
    public const string UserLoggedOut = "user.logged_out";
    public const string AccountCreated = "financial_account.created";
    public const string AccountArchived = "financial_account.archived";

    /// <summary>
    /// §24: la persona declaró cuánto efectivo tiene y el saldo quedó anclado a
    /// esa cifra, sin movimiento de por medio (no había historial que explicar).
    /// </summary>
    public const string CashBalanceAnchored = "cash.balance_anchored";

    /// <summary>
    /// §25: la persona corrigió su efectivo y la diferencia quedó registrada como
    /// un movimiento de ajuste visible. Queda en la auditoría porque es la única
    /// operación que cambia un saldo sin que la persona haya gastado ni recibido
    /// nada -- tiene que poder rastrearse después.
    /// </summary>
    public const string CashBalanceAdjusted = "cash.balance_adjusted";
    public const string AccountDeleted = "financial_account.deleted";
    public const string ImportUploaded = "import.uploaded";
    public const string ImportConfirmed = "import.confirmed";
    public const string EmailConnected = "email_connection.connected";
    public const string EmailDisconnected = "email_connection.disconnected";
    public const string DataExported = "privacy.data_exported";
    public const string TransactionsDeleted = "privacy.transactions_deleted";
    public const string AccountDeletionRequested = "privacy.account_deletion_requested";

    /// <summary>Entregable 22: a login during the grace period undid a pending deletion.</summary>
    public const string AccountDeletionCancelled = "privacy.account_deletion_cancelled";

    /// <summary>Presupuestos: only the budget id is recorded, never its amount or name.</summary>
    public const string BudgetCreated = "budget.created";
    public const string BudgetUpdated = "budget.updated";
    public const string BudgetDeleted = "budget.deleted";

    /// <summary>Tarjetas de crédito: only ids are recorded -- never amounts, digits, debt or merchants.</summary>
    public const string CreditCardConfigured = "credit_card.configured";
    public const string CreditCardRestored = "credit_card.restored";
    public const string CreditCardDebtSet = "credit_card.debt_set";
    public const string CreditCardStatementDeclared = "credit_card.statement_declared";
    public const string CreditCardStatementRemoved = "credit_card.statement_removed";
    public const string CreditCardPaymentRegistered = "credit_card.payment_registered";
    public const string CreditCardPaymentLinked = "credit_card.payment_linked";
    public const string InstallmentPlanCreated = "credit_card.installment_plan_created";
    public const string InstallmentPlanCancelled = "credit_card.installment_plan_cancelled";
    public const string InstallmentPlanDeleted = "credit_card.installment_plan_deleted";
}
