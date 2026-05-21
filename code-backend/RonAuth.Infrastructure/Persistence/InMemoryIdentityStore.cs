using System.Collections.Concurrent;
using RonAuth.Application.Abstractions;
using RonAuth.Application.Models;
using RonAuth.Domain.Sessions;
using RonAuth.Domain.Users;

namespace RonAuth.Infrastructure.Persistence;

public sealed class InMemoryIdentityStore : IUserRepository, IUserAccessRepository, ISessionRepository, IOtpCodeRepository
{
    private readonly ConcurrentDictionary<Guid, User> users = new();
    private readonly ConcurrentDictionary<string, Guid> userNameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, List<UserAccess>> userAccesses = new();
    private readonly ConcurrentDictionary<string, AuthSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, OtpCodeRecord> otpCodes = new();

    public InMemoryIdentityStore(IPasswordHashService passwordHashService)
    {
        var adminUser = new User
        {
            Id = Guid.Parse("4be2af39-79fa-4c13-bae3-f0a3ddfd0d73"),
            UserName = "admin",
            Email = "admin@ronauth.local",
            IsLoginAllowed = true,
            PasswordChangedAtUtc = DateTimeOffset.UtcNow,
        };
        adminUser.PasswordHash = passwordHashService.HashPassword(adminUser, "Admin123!");

        users[adminUser.Id] = adminUser;
        userNameIndex[adminUser.UserName] = adminUser.Id;
        userAccesses[adminUser.Id] =
        [
            new UserAccess
            {
                RoleId = Guid.Parse("8d03b969-4914-4f9f-af39-9b4ff1df8078"),
                RoleName = "SystemAdministrator",
                ScopeId = Guid.Parse("40fef5fc-98af-4318-b23d-8db53dd779e5"),
                ScopeName = "RonFlow",
            },
        ];
    }

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        if (!userNameIndex.TryGetValue(userName, out var userId))
        {
            return Task.FromResult<User?>(null);
        }

        users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task CreateAsync(User user, CancellationToken cancellationToken)
    {
        users[user.Id] = user;
        userNameIndex[user.UserName] = user.Id;
        userAccesses.TryAdd(user.Id, new List<UserAccess>());
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        users[user.Id] = user;
        userNameIndex[user.UserName] = user.Id;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserAccess>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        userAccesses.TryGetValue(userId, out var accesses);
        return Task.FromResult<IReadOnlyList<UserAccess>>(accesses is null ? Array.Empty<UserAccess>() : accesses);
    }

    public Task CreateAsync(AuthSession session, CancellationToken cancellationToken)
    {
        sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task<AuthSession?> GetActiveAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AuthSession?>(null);
        }

        if (session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= currentTime)
        {
            return Task.FromResult<AuthSession?>(null);
        }

        return Task.FromResult<AuthSession?>(session);
    }

    public Task TouchAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        if (sessions.TryGetValue(sessionId, out var session))
        {
            session.LastSeenAtUtc = currentTime;
        }

        return Task.CompletedTask;
    }

    public Task RevokeAsync(string sessionId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        if (sessions.TryGetValue(sessionId, out var session))
        {
            session.RevokedAtUtc = currentTime;
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(OtpCodeRecord code, CancellationToken cancellationToken)
    {
        var existingCodes = otpCodes.Values
            .Where(item => item.UserId == code.UserId && string.Equals(item.ProviderName, code.ProviderName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id)
            .ToArray();

        foreach (var existingCodeId in existingCodes)
        {
            otpCodes.TryRemove(existingCodeId, out _);
        }

        otpCodes[code.Id] = code;
        return Task.CompletedTask;
    }

    public Task<OtpCodeRecord?> GetActiveAsync(Guid userId, string providerName, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        var record = otpCodes.Values
            .Where(item => item.UserId == userId
                && string.Equals(item.ProviderName, providerName, StringComparison.OrdinalIgnoreCase)
                && item.UsedAtUtc is null
                && item.ExpiresAtUtc > currentTime)
            .OrderByDescending(item => item.ExpiresAtUtc)
            .FirstOrDefault();

        return Task.FromResult(record);
    }

    public Task MarkUsedAsync(Guid codeId, DateTimeOffset currentTime, CancellationToken cancellationToken)
    {
        if (otpCodes.TryGetValue(codeId, out var code))
        {
            code.UsedAtUtc = currentTime;
        }

        return Task.CompletedTask;
    }
}