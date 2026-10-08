using Metrics.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var id)) return Unauthorized();
        return Ok(await auth.GetCurrentAsync(id, ct));
    }

    /// <summary>Temporary: exercises the Technical policy until real Technical endpoints exist (remove then).</summary>
    [Authorize(Policy = Policies.Technical)]
    [HttpGet("technical-check")]
    public IActionResult TechnicalCheck() => Ok(new { ok = true });
}
