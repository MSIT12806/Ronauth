namespace RonAuth.Application.Options;

public sealed class JwtSettings
{
    public string Issuer { get; init; } = "RonAuth";
    public string Audience { get; init; } = "RonAuth.Client";
    public string SigningKey { get; init; } = "RonAuth-Development-Signing-Key-Needs-Override";
    public int AccessTokenLifetimeMinutes { get; init; } = 30;
    public int TemporaryTokenLifetimeMinutes { get; init; } = 10;
}