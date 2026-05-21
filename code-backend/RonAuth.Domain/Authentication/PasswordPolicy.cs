namespace RonAuth.Domain.Authentication;

public sealed class PasswordPolicy
{
    public int MinimumLength { get; init; }
    public bool RequireUppercase { get; init; }
    public bool RequireLowercase { get; init; }
    public bool RequireDigit { get; init; }
    public bool RequireNonAlphanumeric { get; init; }
    public int MinimumPasswordAgeDays { get; init; }
    public int MaximumPasswordAgeDays { get; init; }
    public int PasswordHistoryCount { get; init; }
}