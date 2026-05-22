using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RonAuth.Application.Abstractions;
using RonAuth.Infrastructure.Persistence;
using RonAuth.Infrastructure.Security;

namespace RonAuth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRonAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IPasswordHashService, AspNetPasswordHashService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IOtpProvider, DevelopmentEmailOtpProvider>();

        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<InMemoryIdentityStore>();
            services.AddSingleton<IUserRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
            services.AddSingleton<IUserAccessRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
            services.AddSingleton<ISessionRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
            services.AddSingleton<IOtpCodeRepository>(provider => provider.GetRequiredService<InMemoryIdentityStore>());
            return services;
        }

        services.Configure<SqlitePersistenceOptions>(configuration.GetSection(SqlitePersistenceOptions.SectionName));
        services.AddSingleton<SqliteIdentityStore>();
        services.AddSingleton<IUserRepository>(provider => provider.GetRequiredService<SqliteIdentityStore>());
        services.AddSingleton<IUserAccessRepository>(provider => provider.GetRequiredService<SqliteIdentityStore>());
        services.AddSingleton<ISessionRepository>(provider => provider.GetRequiredService<SqliteIdentityStore>());
        services.AddSingleton<IOtpCodeRepository>(provider => provider.GetRequiredService<SqliteIdentityStore>());
        return services;
    }
}