namespace RonAuth.Domain.Sessions;

public sealed class AuthSession
{
    public string SessionId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string IdentityProvider { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}