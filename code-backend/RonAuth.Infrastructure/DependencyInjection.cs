using Microsoft.Extensions.DependencyInjection;
using RonAuth.Application.Abstractions;
using RonAuth.Infrastructure.Persistence;
using RonAuth.Infrastructure.Security;

namespace RonAuth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRonAuthInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHashService, AspNetPasswordHashService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<InMemoryIdentityStore>();
        services.AddSingleton<IUserRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<IUserAccessRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<ISessionRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<IOtpCodeRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
        services.AddSingleton<IOtpProvider, DevelopmentEmailOtpProvider>();
        return services;
    }
}