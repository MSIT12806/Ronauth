using Microsoft.Extensions.Logging;
using RonAuth.Application.Abstractions;
using RonAuth.Domain.Users;

namespace RonAuth.Infrastructure.Security;

public sealed class DevelopmentEmailOtpProvider(ILogger<DevelopmentEmailOtpProvider> logger) : IOtpProvider
{
    public string ProviderName => "email";

    public Task SendAsync(User user, string code, CancellationToken cancellationToken)
    {
        logger.LogInformation("RonAuth OTP for {UserName} <{Email}>: {Code}", user.UserName, user.Email, code);
        return Task.CompletedTask;
    }
}