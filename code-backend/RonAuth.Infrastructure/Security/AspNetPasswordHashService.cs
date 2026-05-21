using Microsoft.AspNetCore.Identity;
using RonAuth.Application.Abstractions;
using RonAuth.Domain.Users;

namespace RonAuth.Infrastructure.Security;

public sealed class AspNetPasswordHashService : IPasswordHashService
{
    private readonly PasswordHasher<User> passwordHasher = new();

    public string HashPassword(User user, string password)
    {
        return passwordHasher.HashPassword(user, password);
    }

    public bool VerifyPassword(User user, string passwordHash, string password)
    {
        var result = passwordHasher.VerifyHashedPassword(user, passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}