using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RonAuth.Api.Contracts;
using RonAuth.Application.Models;
using RonAuth.Application.Options;
using RonAuth.Application.Services;
using RonAuth.Domain.Authentication;

namespace RonAuth.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AuthenticationService authenticationService,
    IdentitySessionService sessionService,
    IOptions<CrossSiteSessionOptions> sessionOptions) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResponse>> LoginAsync(
        [FromBody] PasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(new PasswordLoginCommand(request.UserName, request.Password), cancellationToken);
        return await BuildAuthenticationResponseAsync(result, cancellationToken);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthenticationResponse>> RegisterAsync(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.RegisterAsync(
            new RegisterUserCommand(request.UserName, request.Email, request.Password),
            cancellationToken);

        if (!result.Succeeded || result.Authentication is null)
        {
            return BadRequest(new { errors = result.Errors });
        }

        return await BuildAuthenticationResponseAsync(result.Authentication, cancellationToken);
    }

    [HttpPost("otp/send")]
    public async Task<ActionResult<AuthenticationResponse>> SendOtpAsync(
        [FromBody] SendOtpLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.SendOtpLoginAsync(new SendOtpLoginCommand(request.UserName, request.ProviderName), cancellationToken);
        return StatusCode(result.Status == LoginStatus.RequiresSecondFactor ? StatusCodes.Status202Accepted : StatusCodes.Status400BadRequest, ToResponse(result));
    }

    [HttpPost("second-factor/verify")]
    public async Task<ActionResult<AuthenticationResponse>> VerifySecondFactorAsync(
        [FromBody] VerifySecondFactorRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.VerifySecondFactorAsync(
            new VerifySecondFactorCommand(request.TemporaryToken, request.ProviderName, request.VerificationCode),
            cancellationToken);
        return await BuildAuthenticationResponseAsync(result, cancellationToken);
    }

    [HttpGet("bootstrap")]
    [HttpGet("session-restore")]
    public async Task<ActionResult<AuthenticationResponse>> BootstrapAsync(CancellationToken cancellationToken)
    {
        var session = await GetActiveSessionAsync(cancellationToken);
        if (session is null)
        {
            ClearSessionCookie();
            return Unauthorized();
        }

        var result = await authenticationService.RefreshAccessTokenAsync(session.UserId, cancellationToken);
        if (result.Status != LoginStatus.Success)
        {
            await sessionService.RevokeAsync(session.SessionId, cancellationToken);
            ClearSessionCookie();
            return Unauthorized();
        }

        return Ok(ToResponse(result));
    }

    [Authorize]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthenticationResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await authenticationService.RefreshAccessTokenAsync(userId, cancellationToken);
        return Ok(ToResponse(result));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(sessionOptions.Value.CookieName, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
        {
            await sessionService.RevokeAsync(sessionId, cancellationToken);
        }

        ClearSessionCookie();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponse>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await authenticationService.GetCurrentUserAsync(userId, cancellationToken);
        return user is null ? NotFound() : Ok(ToUserResponse(user));
    }

    [Authorize]
    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var succeeded = await authenticationService.ChangePasswordAsync(
            new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return succeeded ? NoContent() : BadRequest();
    }

    [Authorize]
    [HttpPut("second-factor/{providerName}")]
    public async Task<IActionResult> SetSecondFactorAsync(
        string providerName,
        [FromBody] SetSecondFactorRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var succeeded = await authenticationService.SetSecondFactorAsync(userId, providerName, request.Enabled, cancellationToken);
        return succeeded ? NoContent() : NotFound();
    }

    private async Task<ActionResult<AuthenticationResponse>> BuildAuthenticationResponseAsync(
        AuthenticationResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status == LoginStatus.Success && result.User is not null)
        {
            var session = await sessionService.CreateAsync(result.User.UserId, result.IdentityProvider, result.Subject, cancellationToken);
            WriteSessionCookie(session.SessionId);
            return Ok(ToResponse(result));
        }

        if (result.Status == LoginStatus.RequiresSecondFactor)
        {
            return StatusCode(StatusCodes.Status202Accepted, ToResponse(result));
        }

        return result.Status == LoginStatus.LockedOut
            ? StatusCode(StatusCodes.Status423Locked, ToResponse(result))
            : BadRequest(ToResponse(result));
    }

    private void WriteSessionCookie(string sessionId)
    {
        var options = sessionOptions.Value;
        Response.Cookies.Append(options.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = options.CookieSecure,
            SameSite = options.CookieSecure ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(options.SessionLifetimeMinutes),
        });
    }

    private void ClearSessionCookie()
    {
        Response.Cookies.Delete(sessionOptions.Value.CookieName);
    }

    private async Task<RonAuth.Domain.Sessions.AuthSession?> GetActiveSessionAsync(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(sessionOptions.Value.CookieName, out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return await sessionService.GetActiveAsync(sessionId, cancellationToken);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(rawUserId, out userId);
    }

    private static AuthenticationResponse ToResponse(AuthenticationResult result)
    {
        return new AuthenticationResponse
        {
            Status = result.Status.ToString(),
            FailureCode = result.FailureCode,
            Message = result.Message,
            AccessToken = result.AccessToken,
            TemporaryToken = result.TemporaryToken,
            SecondFactorProvider = result.SecondFactorProvider,
            User = result.User is null ? null : ToUserResponse(result.User),
            Accesses = result.Accesses.Select(item => new UserAccessResponse
            {
                RoleId = item.RoleId,
                RoleName = item.RoleName,
                ScopeId = item.ScopeId,
                ScopeName = item.ScopeName,
            }).ToArray(),
        };
    }

    private static UserProfileResponse ToUserResponse(UserProfile user)
    {
        return new UserProfileResponse
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            EnabledTwoFactorProviders = user.EnabledTwoFactorProviders,
        };
    }
}