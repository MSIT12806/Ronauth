using RonAuth.Application.Models;
using RonAuth.Domain.Sessions;
using RonAuth.Domain.Users;

namespace RonAuth.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task CreateAsync(User user, CancellationToken cancellationToken);
    Task UpdateAsync(User user, CancellationToken cancellationToken);
}

public interface IUserAccessRepository
{
    Task<IReadOnlyList<UserAccess>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ISessionRepository
{
    Task CreateAsync(AuthSession session, CancellationToken cancellationToken);
    Task<AuthSession?> GetActiveAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken);
    Task TouchAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken);
    Task RevokeAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken);
}

public interface IOtpCodeRepository
{
    Task SaveAsync(OtpCodeRecord code, CancellationToken cancellationToken);
    Task<OtpCodeRecord?> GetActiveAsync(Guid userId, string providerName, DateTimeOffset currentTime, CancellationToken cancellationToken);
    Task MarkUsedAsync(Guid codeId, DateTimeOffset currentTime, CancellationToken cancellationToken);
}

public interface IPasswordHashService
{
    string HashPassword(User user, string password);
    bool VerifyPassword(User user, string passwordHash, string password);
}

public interface ITokenService
{
    string GenerateAccessToken(User user, IReadOnlyList<UserAccess> accesses);
    string GenerateTemporaryToken(Guid userId, string providerName, TimeSpan lifetime);
    (Guid UserId, string ProviderName)? ValidateTemporaryToken(string token);
}

public interface IOtpProvider
{
    string ProviderName { get; }
    Task SendAsync(User user, string code, CancellationToken cancellationToken);
}