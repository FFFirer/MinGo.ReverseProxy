using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MinGo.IdentityKit;

namespace MinGo.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/account-administration")]
[Authorize(Policy = "ManageAccountPasswords")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountAdministrationController(
    PasswordResetAdministration<IdentityUser> passwords,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken() =>
        Ok(new { requestToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("users/{userId}/password-reset-token")]
    public async Task<IActionResult> GeneratePasswordResetToken(string userId)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { message = "Invalid CSRF token." });

        var result = await passwords.GenerateTokenAsync(User, userId);
        return result.Status switch
        {
            PasswordResetTokenStatus.Succeeded => Ok(new { token = result.Token }),
            PasswordResetTokenStatus.UserNotFound => NotFound(),
            _ => Forbid()
        };
    }
}
