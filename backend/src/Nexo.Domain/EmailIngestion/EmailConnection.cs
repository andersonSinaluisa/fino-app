using Nexo.Domain.Common;

namespace Nexo.Domain.EmailIngestion;

public enum EmailProviderKind
{
    /// <summary>Gmail via OAuth (read-only scope). Requires Google credentials.</summary>
    Gmail = 0,

    /// <summary>Microsoft 365 / Outlook via OAuth (read-only scope).</summary>
    Outlook = 1,

    /// <summary>User forwards bank notifications to a per-user Nexo inbound address.</summary>
    Forwarding = 2,
}

public enum EmailConnectionStatus
{
    /// <summary>Created, waiting for the user to finish the consent flow.</summary>
    PendingAuthorization = 0,

    Connected = 1,

    /// <summary>Token expired or was revoked upstream; the user must reconnect.</summary>
    NeedsReauthorization = 2,

    /// <summary>User disconnected it. Secrets are wiped, history is kept.</summary>
    Revoked = 3,
}

/// <summary>
/// An authorised mailbox that Nexo may read bank notifications from.
/// Nexo never stores the mailbox password: only an OAuth grant, and that grant is
/// stored as a reference to an encrypted secret, never as clear text.
/// </summary>
public sealed class EmailConnection : Entity, IUserOwned
{
    private EmailConnection()
    {
    }

    public Guid UserId { get; private set; }

    public EmailProviderKind ProviderKind { get; private set; }

    public string EmailAddress { get; private set; } = null!;

    public EmailConnectionStatus Status { get; private set; } = EmailConnectionStatus.PendingAuthorization;

    /// <summary>Opaque handle into the secret store. Never the token itself.</summary>
    public string? SecretReference { get; private set; }

    public string? GrantedScopes { get; private set; }

    /// <summary>Provider-side cursor (Gmail historyId / Graph deltaLink) so syncs are incremental.</summary>
    public string? SyncCursor { get; private set; }

    public DateTimeOffset? ConnectedAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public int MessagesProcessed { get; private set; }

    public int TransactionsDetected { get; private set; }

    /// <summary>Inbound address for forwarding connections, e.g. u-9f0c@in.nexo.app.</summary>
    public string? InboundAddress { get; private set; }

    public static EmailConnection Start(
        Guid userId,
        EmailProviderKind providerKind,
        string emailAddress,
        DateTimeOffset now,
        string? inboundAddress = null)
    {
        var connection = new EmailConnection
        {
            UserId = userId,
            ProviderKind = providerKind,
            EmailAddress = Users.User.NormalizeEmail(emailAddress),
            InboundAddress = inboundAddress,
        };
        connection.Stamp(now);
        return connection;
    }

    public void Authorize(string secretReference, string grantedScopes, DateTimeOffset now)
    {
        SecretReference = DomainException.RequireText(secretReference, nameof(secretReference), 200);
        GrantedScopes = grantedScopes;
        Status = EmailConnectionStatus.Connected;
        ConnectedAt = now;
        RevokedAt = null;
        Stamp(now);
    }

    /// <summary>
    /// Entregable 23 ("Preparar email ingestion"): Forwarding has no consent screen
    /// and no per-connection secret to store -- its security comes from the shared
    /// webhook secret plus the trusted-sender allow-list, not from an OAuth grant --
    /// so there is nothing to wait for. Before this existed, EmailConnectionService's
    /// StartAsync created every connection, Forwarding included, and left it in
    /// <see cref="EmailConnectionStatus.PendingAuthorization"/> forever, since
    /// <see cref="Authorize"/> requires a non-empty secret. This is the
    /// Forwarding-only equivalent of Authorize.
    /// </summary>
    public void ConnectForwarding(DateTimeOffset now)
    {
        Status = EmailConnectionStatus.Connected;
        ConnectedAt = now;
        RevokedAt = null;
        Stamp(now);
    }

    public void RequireReauthorization(DateTimeOffset now)
    {
        Status = EmailConnectionStatus.NeedsReauthorization;
        Stamp(now);
    }

    public void RecordSync(string? cursor, int messagesProcessed, int transactionsDetected, DateTimeOffset now)
    {
        SyncCursor = cursor ?? SyncCursor;
        MessagesProcessed += messagesProcessed;
        TransactionsDetected += transactionsDetected;
        LastSyncedAt = now;
        Stamp(now);
    }

    /// <summary>Disconnect: the secret reference is dropped so the grant becomes unusable.</summary>
    public void Revoke(DateTimeOffset now)
    {
        Status = EmailConnectionStatus.Revoked;
        SecretReference = null;
        SyncCursor = null;
        RevokedAt = now;
        Stamp(now);
    }
}
