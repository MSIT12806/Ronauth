using Microsoft.Extensions.Options;
using RonAuth.Application.Abstractions;
using RonAuth.Application.Options;
using RonAuth.Domain.Sessions;

namespace RonAuth.Application.Services;

public sealed class IdentitySessionService(
    ISessionRepository sessionRepository,
    IOptions<CrossSiteSessionOptions> sessionOptions,
    TimeProvider timeProvider)
{
    public async Task<AuthSession> CreateAsync(
        Guid userId,
        string identityProvider,
        string subject,
        CancellationToken cancellationToken)
    {
        var currentTime = timeProvider.GetUtcNow();
        var session = new AuthSession
        {
            SessionId = $"sid_{Guid.NewGuid():N}",
            UserId = userId,
            IdentityProvider = identityProvider,
            Subject = subject,
            IssuedAtUtc = currentTime,
            ExpiresAtUtc = currentTime.AddMinutes(sessionOptions.Value.SessionLifetimeMinutes),
            LastSeenAtUtc = currentTime,
        };

        await sessionRepository.CreateAsync(session, cancellationToken);
        return session;
    }

    public Task<AuthSession?> GetActiveAsync(string sessionId, CancellationToken cancellationToken)
    {
        return sessionRepository.GetActiveAsync(sessionId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task TouchAsync(string sessionId, CancellationToken cancellationToken)
    {
        return sessionRepository.TouchAsync(sessionId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task RevokeAsync(string sessionId, CancellationToken cancellationToken)
    {
        return sessionRepository.RevokeAsync(sessionId, timeProvider.GetUtcNow(), cancellationToken);
    }
}