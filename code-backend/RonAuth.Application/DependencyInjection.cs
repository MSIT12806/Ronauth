using Microsoft.Extensions.DependencyInjection;
using RonAuth.Application.Services;

namespace RonAuth.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRonAuthApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationService>();
        services.AddScoped<IdentitySessionService>();
        return services;
    }
}