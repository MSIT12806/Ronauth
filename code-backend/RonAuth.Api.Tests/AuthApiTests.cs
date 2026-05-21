using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RonAuth.Api.Contracts;

namespace RonAuth.Api.Tests;

public sealed class AuthApiTests
{
    private WebApplicationFactory<Program>? factory;

    [SetUp]
    public void SetUp()
    {
        factory = new WebApplicationFactory<Program>();
    }

    [TearDown]
    public void TearDown()
    {
        factory?.Dispose();
    }

    [Test]
    public async Task PasswordLogin_WithSeededAdmin_ReturnsAccessTokenAndSessionCookie()
    {
        using var client = factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new PasswordLoginRequest("admin", "Admin123!"));
        var payload = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload!.AccessToken, Is.Not.Empty);
            Assert.That(payload.User?.UserName, Is.EqualTo("admin"));
            Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(item => item.Contains("ronauth.sid=", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }
}