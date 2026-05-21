namespace RonAuth.Domain.Users;

public sealed class UserTwoFactorMethod
{
    public string ProviderName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string Secret { get; set; } = string.Empty;
}