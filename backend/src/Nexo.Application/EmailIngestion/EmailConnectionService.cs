using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.EmailIngestion;

namespace Nexo.Application.EmailIngestion;

public sealed record EmailConnectionDto(
    Guid Id,
    string ProviderKind,
    string EmailAddress,
    string Status,
    string? InboundAddress,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSyncedAt,
    int TransactionsDetected,
    bool IsAvailable,
    string? UnavailableReason);

public sealed record StartEmailConnectionRequest(string ProviderKind, string EmailAddress);

public interface IEmailConnectionService
{
    Task<IReadOnlyList<EmailConnectionDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<EmailConnectionDto> StartAsync(Guid userId, StartEmailConnectionRequest request, CancellationToken cancellationToken);

    Task RevokeAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken);
}

/// <summary>
/// Manages mailbox authorisations. OAuth for Gmail and Outlook needs client
/// credentials that are not part of the repository, so the corresponding modes
/// report themselves as unavailable until configured — the app never shows a
/// connect button that leads nowhere.
/// </summary>
public sealed class EmailConnectionService(
    INexoDbContext db,
    IEmailProviderAvailability availability,
    IClock clock) : IEmailConnectionService
{
    public async Task<IReadOnlyList<EmailConnectionDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connections = await db.EmailConnections
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Status != EmailConnectionStatus.Revoked)
            .ToListAsync(cancellationToken);

        return connections.Select(Map).ToList();
    }

    public async Task<EmailConnectionDto> StartAsync(
        Guid userId,
        StartEmailConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmailProviderKind>(request.ProviderKind, ignoreCase: true, out var kind))
        {
            throw ValidationException.For(nameof(request.ProviderKind), "Proveedor de correo no válido.");
        }

        if (!availability.IsConfigured(kind))
        {
            throw new ConflictException(
                $"La conexión con {kind} todavía no está habilitada en este entorno.");
        }

        var now = clock.UtcNow;
        var inbound = kind == EmailProviderKind.Forwarding ? BuildInboundAddress(userId) : null;

        var connection = EmailConnection.Start(userId, kind, request.EmailAddress, now, inbound);
        db.EmailConnections.Add(connection);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.EmailConnected,
            nameof(EmailConnection),
            now,
            connection.Id.ToString()));

        await db.SaveChangesAsync(cancellationToken);
        return Map(connection);
    }

    public async Task RevokeAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await db.EmailConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("EmailConnection", connectionId);

        var now = clock.UtcNow;
        connection.Revoke(now);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.EmailDisconnected,
            nameof(EmailConnection),
            now,
            connection.Id.ToString()));

        await db.SaveChangesAsync(cancellationToken);
    }

    private EmailConnectionDto Map(EmailConnection connection) => new(
        connection.Id,
        connection.ProviderKind.ToString(),
        connection.EmailAddress,
        connection.Status.ToString(),
        connection.InboundAddress,
        connection.ConnectedAt,
        connection.LastSyncedAt,
        connection.TransactionsDetected,
        availability.IsConfigured(connection.ProviderKind),
        availability.IsConfigured(connection.ProviderKind) ? null : "Falta configurar las credenciales del proveedor.");

    private static string BuildInboundAddress(Guid userId) =>
        $"u-{userId:N}@in.nexo.app"[..Math.Min(64, $"u-{userId:N}@in.nexo.app".Length)];
}

/// <summary>
/// Tells the rest of the system which mailbox integrations actually have
/// credentials in this environment. Implemented in Infrastructure by reading
/// configuration; never hardcoded to "true".
/// </summary>
public interface IEmailProviderAvailability
{
    bool IsConfigured(EmailProviderKind kind);
}
