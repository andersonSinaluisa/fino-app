using Nexo.Domain.Common;

namespace Nexo.Domain.Imports;

/// <summary>
/// Entregable 10 (Generic CSV mapper): the column choices a user made once,
/// by hand, for a file that no parser could recognise on its own -- saved so
/// the next statement from the same institution imports without asking
/// again. One per (UserId, ProviderCode); a re-mapping replaces it rather
/// than accumulating a history, because only the latest choice should ever
/// apply.
/// </summary>
public sealed class ImportColumnMapping : Entity, IUserOwned
{
    private ImportColumnMapping()
    {
    }

    public Guid UserId { get; private set; }

    public string ProviderCode { get; private set; } = null!;

    /// <summary>Whether row 0 of the file is a header to skip rather than the first movement.</summary>
    public bool FirstRowIsHeader { get; private set; }

    public int DateColumn { get; private set; }

    public int DescriptionColumn { get; private set; }

    /// <summary>Set when the file has one signed amount column. Mutually exclusive with Debit/Credit in practice, but nothing here forbids both being set.</summary>
    public int? AmountColumn { get; private set; }

    public int? DebitColumn { get; private set; }

    public int? CreditColumn { get; private set; }

    public int? ReferenceColumn { get; private set; }

    public static ImportColumnMapping Create(
        Guid userId,
        string providerCode,
        bool firstRowIsHeader,
        int dateColumn,
        int descriptionColumn,
        int? amountColumn,
        int? debitColumn,
        int? creditColumn,
        int? referenceColumn,
        DateTimeOffset now)
    {
        var mapping = new ImportColumnMapping
        {
            UserId = userId,
            ProviderCode = DomainException.RequireText(providerCode, nameof(providerCode), 40),
        };

        mapping.Replace(
            firstRowIsHeader, dateColumn, descriptionColumn, amountColumn, debitColumn, creditColumn, referenceColumn, now);

        return mapping;
    }

    public void Replace(
        bool firstRowIsHeader,
        int dateColumn,
        int descriptionColumn,
        int? amountColumn,
        int? debitColumn,
        int? creditColumn,
        int? referenceColumn,
        DateTimeOffset now)
    {
        if (dateColumn < 0 || descriptionColumn < 0)
        {
            throw new DomainException(
                "mapping_missing_required_column",
                "La columna de fecha y la de descripción son obligatorias.");
        }

        if (amountColumn is null && debitColumn is null && creditColumn is null)
        {
            throw new DomainException(
                "mapping_missing_amount",
                "Se necesita la columna de monto, o las de débito/crédito.");
        }

        FirstRowIsHeader = firstRowIsHeader;
        DateColumn = dateColumn;
        DescriptionColumn = descriptionColumn;
        AmountColumn = amountColumn;
        DebitColumn = debitColumn;
        CreditColumn = creditColumn;
        ReferenceColumn = referenceColumn;
        Stamp(now);
    }
}
