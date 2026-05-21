using RonAuth.Domain.Authentication;
using RonAuth.Domain.Users;

namespace RonAuth.Application.Models;

public sealed record PasswordLoginCommand(string UserName, string Password);

public sealed record SendOtpLoginCommand(string UserName, string ProviderName = "email");

public sealed record VerifySecondFactorCommand(string TemporaryToken, string ProviderName, string VerificationCode);

public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword);

public sealed class AuthenticationResult
{
    public LoginStatus Status { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string TemporaryToken { get; init; } = string.Empty;
    public string IdentityProvider { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string SecondFactorProvider { get; init; } = string.Empty;
    public UserProfile? User { get; init; }
    public IReadOnlyList<UserAccess> Accesses { get; init; } = Array.Empty<UserAccess>();
}

public sealed class UserProfile
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public IReadOnlyList<string> EnabledTwoFactorProviders { get; init; } = Array.Empty<string>();
}