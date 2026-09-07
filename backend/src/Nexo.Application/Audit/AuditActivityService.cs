using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Audit;

namespace Nexo.Application.Audit;

/// <summary>
/// Entregable 21 ("Auditoría"): one row of the user's own security/account
/// activity, in plain Spanish. <see cref="Action"/> is the raw code
/// (<c>AuditActions.*</c>) so the client can pick an icon; <see cref="Label"/>
/// is what actually gets shown, so a client never has to duplicate the
/// action-to-sentence mapping.
/// </summary>
public sealed record SecurityEventDto(Guid Id, string Action, string Label, DateTimeOffset CreatedAt, bool Succeeded);

public interface IAuditActivityService
{
    /// <summary>The user's own audit trail, newest first.</summary>
    Task<PagedResult<SecurityEventDto>> ListAsync(Guid userId, PageRequest page, CancellationToken cancellationToken);
}

public sealed class AuditActivityService(INexoDbContext db) : IAuditActivityService
{
    public async Task<PagedResult<SecurityEventDto>> ListAsync(
        Guid userId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        // AuditLogEntry.UserId is nullable -- a failed login against an email
        // that doesn't exist has no user to attach to -- so, unlike every
        // IUserOwned entity, it never joins Nexo's automatic per-user query
        // filter (ADR-006). This explicit predicate is therefore the ONLY thing
        // standing between one user and every other user's history here, not a
        // redundant second layer, and it deliberately uses plain equality
        // (never a client-side Contains against an array) -- see
        // QueryableGuidExtensions' remarks on which query shapes fail to
        // translate against SQLite once a global filter is in the mix.
        var query = db.AuditLog.AsNoTracking()
            .Where(a => a.UserId == userId
                // Routine token refresh happens every few minutes of normal app
                // use and the "uploaded" half of an import is always followed
                // by either "confirmed" or nothing -- both would flood this
                // list without telling the person anything they can act on.
                && a.Action != AuditActions.TokenRefreshed
                && a.Action != AuditActions.ImportUploaded);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return PagedResult<SecurityEventDto>.Empty(page.NormalizedPageSize);
        }

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(a => new SecurityEventDto(a.Id, a.Action, LabelFor(a.Action), a.CreatedAt, a.Succeeded))
            .ToList();

        return new PagedResult<SecurityEventDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    private static string LabelFor(string action) => action switch
    {
        AuditActions.UserRegistered => "Creaste tu cuenta Nexo.",
        AuditActions.UserLoggedIn => "Iniciaste sesión.",
        AuditActions.UserLoginFailed => "Alguien intentó iniciar sesión con la contraseña incorrecta.",
        AuditActions.AccountLocked => "Tu cuenta se bloqueó temporalmente por varios intentos fallidos.",
        AuditActions.UserLoginBlocked => "Se intentó iniciar sesión mientras tu cuenta estaba bloqueada.",
        AuditActions.TokenReuseDetected => "Detectamos actividad sospechosa y cerramos todas tus sesiones por seguridad.",
        AuditActions.UserLoggedOut => "Se cerró una sesión.",
        AuditActions.AccountCreated => "Agregaste una cuenta financiera.",
        AuditActions.AccountArchived => "Archivaste una cuenta financiera.",
        AuditActions.AccountDeleted => "Eliminaste una cuenta financiera y su historial.",
        AuditActions.ImportConfirmed => "Confirmaste una importación de movimientos.",
        AuditActions.EmailConnected => "Conectaste un correo para detectar movimientos.",
        AuditActions.EmailDisconnected => "Desconectaste un correo.",
        AuditActions.DataExported => "Exportaste tus datos.",
        AuditActions.TransactionsDeleted => "Eliminaste movimientos.",
        AuditActions.AccountDeletionRequested => "Solicitaste eliminar tu cuenta Nexo.",
        AuditActions.AccountDeletionCancelled => "Cancelaste la eliminación de tu cuenta al iniciar sesión de nuevo.",

        // A future action this mapping hasn't been taught yet -- shown as-is
        // rather than hidden, so nothing silently disappears from the trail.
        _ => action,
    };
}
