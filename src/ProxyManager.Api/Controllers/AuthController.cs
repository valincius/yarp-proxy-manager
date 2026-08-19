using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Infrastructure.Persistence;

namespace ProxyManager.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        return Ok(await SessionAsync());
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        return Ok(await SessionAsync());
    }

    [HttpGet("antiforgery")]
    [AllowAnonymous]
    public IActionResult AntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpGet("external-enabled")]
    [AllowAnonymous]
    public IActionResult ExternalEnabled(IConfiguration configuration)
        => Ok(new { enabled = !string.IsNullOrWhiteSpace(configuration["Oidc:Authority"]) });

    [HttpGet("external-login")]
    [AllowAnonymous]
    public IActionResult ExternalLogin([FromQuery] string? returnUrl = "/admin")
    {
        var redirectUri = Url.Action(nameof(ExternalCallback), "Auth", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUri };
        return Challenge(properties, "oidc");
    }

    [HttpGet("external-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCallback([FromQuery] string? returnUrl = "/admin")
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return BadRequest(new { error = "External login failed." });
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var user = email is not null ? await userManager.FindByEmailAsync(email) : null;
        if (user is null && email is not null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = email.Split('@')[0],
                EmailConfirmed = true,
            };

            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                return BadRequest(new { error = "Could not provision the OIDC user." });
            }

            await userManager.AddToRoleAsync(user, "User");
            await userManager.AddLoginAsync(user, info);
        }

        if (user is null)
        {
            return BadRequest(new { error = "The OIDC login could not be linked to a user." });
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return LocalRedirect(returnUrl ?? "/admin");
    }

    private async Task<object> SessionAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return new { authenticated = false };
        }

        var roles = await userManager.GetRolesAsync(user);
        return new
        {
            authenticated = true,
            email = user.Email,
            displayName = user.DisplayName,
            roles,
        };
    }
}

public sealed record LoginRequest(string Email, string Password);
