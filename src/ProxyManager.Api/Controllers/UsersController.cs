using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProxyManager.Application.Exceptions;
using ProxyManager.Infrastructure.Persistence;

namespace ProxyManager.Api.Controllers;

[Route("api/v1/users")]
[Authorize(Roles = "Admin")]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = userManager.Users.OrderBy(u => u.Email).ToList();
        var result = new List<object>();
        foreach (var user in users)
        {
            result.Add(new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.LockoutEnabled,
                user.LockoutEnd,
                Roles = await userManager.GetRolesAsync(user),
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.Email.Split('@')[0],
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
        }

        var role = request.Role ?? "User";
        if (await roleManager.RoleExistsAsync(role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return Ok(new { user.Id, user.Email, user.DisplayName, Roles = await userManager.GetRolesAsync(user) });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (User.Identity?.Name is { } currentEmail)
        {
            var current = await userManager.FindByEmailAsync(currentEmail);
            if (current?.Id == id)
            {
                return BadRequest(new { error = "You cannot delete your own account." });
            }
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(new { error = "Delete failed." });
    }

    [HttpPatch("{id:guid}/enable")]
    public async Task<IActionResult> SetEnabled(Guid id, SetEnabledRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException($"User '{id}' was not found.");
        if (request.Enabled)
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }
        else
        {
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException($"User '{id}' was not found.");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        return result.Succeeded ? NoContent() : BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });
    }
}

public sealed record CreateUserRequest(string Email, string Password, string? Role);

public sealed record ResetPasswordRequest(string Password);
