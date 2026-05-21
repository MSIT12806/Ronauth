using Microsoft.Extensions.Options;
using RonAuth.Application.Abstractions;
using RonAuth.Application.Models;
using RonAuth.Application.Options;
using RonAuth.Domain.Authentication;
using RonAuth.Domain.Users;

namespace RonAuth.Application.Services;

public sealed class AuthenticationService(
    IUserRepository userRepository,
    IUserAccessRepository userAccessRepository,
    IOtpCodeRepository otpCodeRepository,
    IPasswordHashService passwordHashService,
    ITokenService tokenService,
    IEnumerable<IOtpProvider> otpProviders,
    IOptions<PasswordPolicy> passwordPolicyOptions,
    IOptions<JwtSettings> jwtSettings,
    TimeProvider timeProvider)
{
    public async Task<AuthenticationResult> LoginAsync(PasswordLoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByUserNameAsync(command.UserName, cancellationToken);
        var failure = await ValidateAccountAsync(user, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!passwordHashService.VerifyPassword(user!, user.PasswordHash, command.Password))
        {
            return await RegisterFailureAsync(user, LoginFailureCatalog.AccountErrorCode, cancellationToken);
        }

        await ResetFailuresAsync(user!, cancellationToken);

        var secondFactor = user!.TwoFactorMethods.FirstOrDefault(item => item.IsEnabled);
        if (secondFactor is not null)
        {
            return await IssueSecondFactorChallengeAsync(user, secondFactor.ProviderName, cancellationToken);
        }

        return await BuildSuccessAsync(user, "password", user.UserName, cancellationToken);
    }

    public async Task<AuthenticationResult> SendOtpLoginAsync(SendOtpLoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByUserNameAsync(command.UserName, cancellationToken);
        var failure = await ValidateAccountAsync(user, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        return await IssueSecondFactorChallengeAsync(user!, command.ProviderName, cancellationToken);
    }

    public async Task<AuthenticationResult> VerifySecondFactorAsync(VerifySecondFactorCommand command, CancellationToken cancellationToken)
    {
        var temporaryIdentity = tokenService.ValidateTemporaryToken(command.TemporaryToken);
        if (temporaryIdentity is null || !string.Equals(temporaryIdentity.Value.ProviderName, command.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.VerificationExpiredCode);
        }

        var user = await userRepository.GetByIdAsync(temporaryIdentity.Value.UserId, cancellationToken);
        if (user is null)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.AccountErrorCode);
        }

        var code = await otpCodeRepository.GetActiveAsync(user.Id, command.ProviderName, timeProvider.GetUtcNow(), cancellationToken);
        if (code is null)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.VerificationExpiredCode);
        }

        if (!string.Equals(code.Code, command.VerificationCode, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.OtpInvalidCode);
        }

        await otpCodeRepository.MarkUsedAsync(code.Id, timeProvider.GetUtcNow(), cancellationToken);
        await ResetFailuresAsync(user, cancellationToken);
        return await BuildSuccessAsync(user, command.ProviderName, user.Email, cancellationToken);
    }

    public async Task<AuthenticationResult> RefreshAccessTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsLoginAllowed)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.AccountErrorCode);
        }

        return await BuildSuccessAsync(user, "session", user.UserName, cancellationToken);
    }

    public async Task<UserProfile?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : ToUserProfile(user);
    }

    public async Task<bool> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        if (!passwordHashService.VerifyPassword(user, user.PasswordHash, command.CurrentPassword))
        {
            return false;
        }

        if (!ValidatePassword(command.NewPassword, out _))
        {
            return false;
        }

        if (user.PasswordHistoryHashes.Any(item => passwordHashService.VerifyPassword(user, item, command.NewPassword)))
        {
            return false;
        }

        var newHash = passwordHashService.HashPassword(user, command.NewPassword);
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            user.PasswordHistoryHashes.Insert(0, user.PasswordHash);
        }

        var historyLimit = Math.Max(0, passwordPolicyOptions.Value.PasswordHistoryCount);
        if (historyLimit > 0 && user.PasswordHistoryHashes.Count > historyLimit)
        {
            user.PasswordHistoryHashes = user.PasswordHistoryHashes.Take(historyLimit).ToList();
        }

        user.PasswordHash = newHash;
        user.PasswordChangedAtUtc = timeProvider.GetUtcNow();
        await userRepository.UpdateAsync(user, cancellationToken);
        return true;
    }

    public async Task<bool> SetSecondFactorAsync(Guid userId, string providerName, bool enabled, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var method = user.TwoFactorMethods.FirstOrDefault(item => string.Equals(item.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            method = new UserTwoFactorMethod
            {
                ProviderName = providerName,
                Secret = Guid.NewGuid().ToString("N"),
            };

            user.TwoFactorMethods.Add(method);
        }

        method.IsEnabled = enabled;
        await userRepository.UpdateAsync(user, cancellationToken);
        return true;
    }

    private async Task<AuthenticationResult?> ValidateAccountAsync(User? user, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.AccountErrorCode);
        }

        if (!user.IsLoginAllowed)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.AccountDisabledCode);
        }

        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > timeProvider.GetUtcNow())
        {
            return Fail(LoginStatus.LockedOut, LoginFailureCatalog.TooManyFailuresCode);
        }

        if (IsPasswordExpired(user))
        {
            return new AuthenticationResult
            {
                Status = LoginStatus.Failed,
                FailureCode = LoginFailureCatalog.AccountErrorCode,
                Message = "密碼已超過有效期限，請先變更密碼。",
            };
        }

        await ResetLockIfExpiredAsync(user, cancellationToken);
        return null;
    }

    private async Task ResetLockIfExpiredAsync(User user, CancellationToken cancellationToken)
    {
        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value <= timeProvider.GetUtcNow())
        {
            user.LockedUntilUtc = null;
            user.FailedLoginCount = 0;
            await userRepository.UpdateAsync(user, cancellationToken);
        }
    }

    private bool IsPasswordExpired(User user)
    {
        var maximumAge = passwordPolicyOptions.Value.MaximumPasswordAgeDays;
        if (maximumAge <= 0 || user.PasswordChangedAtUtc is null)
        {
            return false;
        }

        return user.PasswordChangedAtUtc.Value.AddDays(maximumAge) <= timeProvider.GetUtcNow();
    }

    private bool ValidatePassword(string password, out string message)
    {
        var policy = passwordPolicyOptions.Value;
        if (password.Length < policy.MinimumLength)
        {
            message = "密碼長度不足";
            return false;
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            message = "密碼必須包含大寫字母";
            return false;
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            message = "密碼必須包含小寫字母";
            return false;
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            message = "密碼必須包含數字";
            return false;
        }

        if (policy.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            message = "密碼必須包含特殊字元";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private async Task<AuthenticationResult> IssueSecondFactorChallengeAsync(User user, string providerName, CancellationToken cancellationToken)
    {
        var provider = otpProviders.FirstOrDefault(item => string.Equals(item.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            return Fail(LoginStatus.Failed, LoginFailureCatalog.ExceptionCode);
        }

        var currentTime = timeProvider.GetUtcNow();
        var code = CreateVerificationCode();
        await otpCodeRepository.SaveAsync(
            new OtpCodeRecord
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                ProviderName = providerName,
                Code = code,
                ExpiresAtUtc = currentTime.AddMinutes(jwtSettings.Value.TemporaryTokenLifetimeMinutes),
            },
            cancellationToken);

        await provider.SendAsync(user, code, cancellationToken);

        return new AuthenticationResult
        {
            Status = LoginStatus.RequiresSecondFactor,
            Message = "需要進行第二因子驗證",
            TemporaryToken = tokenService.GenerateTemporaryToken(
                user.Id,
                providerName,
                TimeSpan.FromMinutes(jwtSettings.Value.TemporaryTokenLifetimeMinutes)),
            SecondFactorProvider = providerName,
            User = ToUserProfile(user),
        };
    }

    private async Task<AuthenticationResult> BuildSuccessAsync(User user, string identityProvider, string subject, CancellationToken cancellationToken)
    {
        var accesses = await userAccessRepository.GetByUserIdAsync(user.Id, cancellationToken);
        return new AuthenticationResult
        {
            Status = LoginStatus.Success,
            Message = "登入成功",
            AccessToken = tokenService.GenerateAccessToken(user, accesses),
            IdentityProvider = identityProvider,
            Subject = subject,
            User = ToUserProfile(user),
            Accesses = accesses,
        };
    }

    private async Task<AuthenticationResult> RegisterFailureAsync(User user, string failureCode, CancellationToken cancellationToken)
    {
        user.FailedLoginCount += 1;
        if (user.FailedLoginCount >= LoginFailureCatalog.MaxFailedAttempts)
        {
            user.LockedUntilUtc = timeProvider.GetUtcNow().AddMinutes(LoginFailureCatalog.LockoutMinutes);
        }

        await userRepository.UpdateAsync(user, cancellationToken);
        return Fail(
            user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > timeProvider.GetUtcNow()
                ? LoginStatus.LockedOut
                : LoginStatus.Failed,
            user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > timeProvider.GetUtcNow()
                ? LoginFailureCatalog.TooManyFailuresCode
                : failureCode);
    }

    private async Task ResetFailuresAsync(User user, CancellationToken cancellationToken)
    {
        if (user.FailedLoginCount == 0 && user.LockedUntilUtc is null)
        {
            return;
        }

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await userRepository.UpdateAsync(user, cancellationToken);
    }

    private static AuthenticationResult Fail(LoginStatus status, string failureCode)
    {
        return new AuthenticationResult
        {
            Status = status,
            FailureCode = failureCode,
            Message = LoginFailureCatalog.ToDisplayMessage(failureCode),
        };
    }

    private static string CreateVerificationCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }

    private static UserProfile ToUserProfile(User user)
    {
        return new UserProfile
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            EnabledTwoFactorProviders = user.TwoFactorMethods
                .Where(item => item.IsEnabled)
                .Select(item => item.ProviderName)
                .ToArray(),
        };
    }
}