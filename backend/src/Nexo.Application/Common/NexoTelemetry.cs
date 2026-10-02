using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nexo.Application.Common;

/// <summary>
/// Entregable 28 ("Observabilidad"): tracing spans and business metrics built on
/// the BCL's <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> -- both are OpenTelemetry-compatible
/// without adding an OpenTelemetry package (ADR-002, docs/architecture.md: minimal
/// dependency surface). docs/deployment.md and docs/roadmap.md already documented
/// "el código usa ActivitySource y Meter de la BCL" as if this existed; it did not
/// anywhere in the codebase until now -- this class is what makes that claim true,
/// not just a fresh promise for later.
///
/// An <see cref="ActivitySource"/> with no registered listener, and a
/// <see cref="Meter"/> with no registered listener, are both near-zero-cost no-ops
/// (StartActivity returns null; Counter.Add is a cheap check-and-return). So this is
/// safe to leave wired in production before an OpenTelemetry exporter is ever
/// configured -- exactly the deferred step docs/deployment.md's "Observabilidad"
/// section already describes.
///
/// Only counts and enum-typed outcomes are recorded here -- never an amount, a
/// description, an email address, a file name or any other value the project's
/// logging discipline (docs/security.md) already keeps out of logs and out of the
/// audit trail (Nexo.Domain.Audit.AuditLogEntry).
/// </summary>
public static class NexoTelemetry
{
    public const string SourceName = "Nexo";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    private static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Tagged by <c>outcome</c> (EmailIngestionOutcome, e.g. "Created", "Duplicate", "Rejected").</summary>
    public static readonly Counter<long> EmailsIngested = Meter.CreateCounter<long>(
        "nexo.email_ingestion.processed",
        unit: "{message}",
        description: "Emails processed by EmailIngestionPipeline, by outcome.");

    /// <summary>Tagged by <c>outcome</c>: "imported", "flagged_for_review" or "upgraded" (see ImportService.ConfirmAsync).</summary>
    public static readonly Counter<long> ImportRowsProcessed = Meter.CreateCounter<long>(
        "nexo.import.rows_processed",
        unit: "{row}",
        description: "Statement rows written when an import is confirmed, by outcome.");

    /// <summary>Tagged by <c>match_type</c> (DuplicateMatchType: ExactMatch, ProbableMatch, NoMatch).</summary>
    public static readonly Counter<long> DeduplicationChecks = Meter.CreateCounter<long>(
        "nexo.deduplication.checks",
        unit: "{check}",
        description: "Deduplication checks performed by DeduplicationService, by match type.");

    /// <summary>Tagged by <c>action</c>: "created", "updated" or "deleted". Never the amount or the name.</summary>
    public static readonly Counter<long> BudgetsChanged = Meter.CreateCounter<long>(
        "nexo.budgets.changed",
        unit: "{budget}",
        description: "Budget definitions created, updated or deleted.");

    /// <summary>
    /// Tarjetas de crédito. Tagged by <c>action</c> only ("created", "payment_registered",
    /// "installment_created", "statement_declared"...). Never an amount, the last four
    /// digits, the issuer, the debt or a merchant.
    /// </summary>
    public static readonly Counter<long> CreditCardEvents = Meter.CreateCounter<long>(
        "nexo.credit_cards.events",
        unit: "{event}",
        description: "Credit-card actions performed by people.");
}
