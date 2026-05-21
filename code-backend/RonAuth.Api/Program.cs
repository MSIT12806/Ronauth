using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using RonAuth.Application;
using RonAuth.Application.Options;
using RonAuth.Application.Services;
using RonAuth.Domain.Authentication;
using RonAuth.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRonAuthApplication();
builder.Services.AddRonAuthInfrastructure();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<CrossSiteSessionOptions>(builder.Configuration.GetSection("CrossSiteSession"));
builder.Services.Configure<PasswordPolicy>(builder.Configuration.GetSection("PasswordPolicy"));

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessionOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<CrossSiteSessionOptions>>().Value;
                var sessionId = context.HttpContext.Request.Cookies[sessionOptions.CookieName];
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    context.Fail("Missing session cookie.");
                    return;
                }

                var sessionService = context.HttpContext.RequestServices.GetRequiredService<IdentitySessionService>();
                var session = await sessionService.GetActiveAsync(sessionId, context.HttpContext.RequestAborted);
                if (session is null)
                {
                    context.Fail("Session is invalid.");
                    return;
                }

                var principal = context.Principal;
                var rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                if (!Guid.TryParse(rawUserId, out var userId) || userId != session.UserId)
                {
                    context.Fail("Session user mismatch.");
                    return;
                }

                await sessionService.TouchAsync(sessionId, context.HttpContext.RequestAborted);
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
