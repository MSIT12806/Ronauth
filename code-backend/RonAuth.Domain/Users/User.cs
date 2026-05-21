namespace RonAuth.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsLoginAllowed { get; set; } = true;
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<UserTwoFactorMethod> TwoFactorMethods { get; set; } = new();
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? PasswordChangedAtUtc { get; set; }
    public List<string> PasswordHistoryHashes { get; set; } = new();
}