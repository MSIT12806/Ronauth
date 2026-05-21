namespace RonAuth.Application.Options;

public sealed class CrossSiteSessionOptions
{
    public string CookieName { get; init; } = "ronauth.sid";
    public int SessionLifetimeMinutes { get; init; } = 480;
    public bool CookieSecure { get; init; } = false;
}