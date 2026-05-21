namespace RonAuth.Api.Contracts;

public sealed record PasswordLoginRequest(string UserName, string Password);

public sealed record SendOtpLoginRequest(string UserName, string ProviderName = "email");

public sealed record VerifySecondFactorRequest(string TemporaryToken, string ProviderName, string VerificationCode);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetSecondFactorRequest(bool Enabled);

public sealed class AuthenticationResponse
{
    public string Status { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string TemporaryToken { get; init; } = string.Empty;
    public string SecondFactorProvider { get; init; } = string.Empty;
    public UserProfileResponse? User { get; init; }
    public IReadOnlyList<UserAccessResponse> Accesses { get; init; } = Array.Empty<UserAccessResponse>();
}

public sealed class UserProfileResponse
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public IReadOnlyList<string> EnabledTwoFactorProviders { get; init; } = Array.Empty<string>();
}

public sealed class UserAccessResponse
{
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid ScopeId { get; init; }
    public string ScopeName { get; init; } = string.Empty;
}