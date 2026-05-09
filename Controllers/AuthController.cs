using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PCPlus.Dashboard.Data;

namespace PCPlus.Dashboard.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly DashboardDb _db;

    public AuthController(DashboardDb db)
    {
        _db = db;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Password))
        ).ToLowerInvariant();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.PasswordHash == hash);

        if (user is null)
            return Unauthorized(new { ok = false, error = "Invalid credentials" });

        // Portal mode: only allow customer role users
        var isPortal = HttpContext.Items.ContainsKey("PortalMode");
        if (isPortal && user.Role != "customer")
            return Unauthorized(new { ok = false, error = "Please use the admin dashboard for this account" });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("DisplayName", user.DisplayName),
            new("CustomerName", user.CustomerName ?? "")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, displayName = user.DisplayName, role = user.Role, customerName = user.CustomerName });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("portal-mode")]
    public IActionResult GetPortalMode()
    {
        var isPortal = HttpContext.Items.ContainsKey("PortalMode");
        return Ok(new { portalMode = isPortal });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        return Ok(new
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            username = User.FindFirstValue(ClaimTypes.Name),
            role = User.FindFirstValue(ClaimTypes.Role),
            displayName = User.FindFirstValue("DisplayName"),
            customerName = User.FindFirstValue("CustomerName")
        });
    }

    [Authorize]
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Role,
                u.DisplayName,
                u.LastLogin,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [Authorize]
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { ok = false, error = "Username and password are required" });

        var pwErr = ValidatePasswordComplexity(request.Password);
        if (pwErr != null)
            return BadRequest(new { ok = false, error = pwErr });

        var exists = await _db.Users.AnyAsync(u => u.Username == request.Username);
        if (exists)
            return Conflict(new { ok = false, error = "Username already exists" });

        var hash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Password))
        ).ToLowerInvariant();

        var user = new PCPlus.Dashboard.Models.DashboardUser
        {
            Username = request.Username,
            PasswordHash = hash,
            Role = request.Role ?? "viewer",
            DisplayName = request.DisplayName ?? request.Username,
            CustomerName = request.CustomerName ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            user = new
            {
                user.Id,
                user.Username,
                user.Role,
                user.DisplayName,
                user.CreatedAt
            }
        });
    }

    [Authorize]
    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { ok = false, error = "User not found" });

        if (request.Role is not null)
            user.Role = request.Role;

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;

        if (request.CustomerName is not null)
            user.CustomerName = request.CustomerName;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            user = new
            {
                user.Id,
                user.Username,
                user.Role,
                user.DisplayName,
                user.LastLogin,
                user.CreatedAt
            }
        });
    }

    [Authorize]
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { ok = false, error = "User not found" });

        if (user.Role == "admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "admin");
            if (adminCount <= 1)
                return BadRequest(new { ok = false, error = "Cannot delete the last admin user" });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdStr is null || !int.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return Unauthorized();

        var currentHash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.CurrentPassword))
        ).ToLowerInvariant();

        if (user.PasswordHash != currentHash)
            return BadRequest(new { ok = false, error = "Current password is incorrect" });

        var err = ValidatePasswordComplexity(request.NewPassword);
        if (err != null)
            return BadRequest(new { ok = false, error = err });

        var newHash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.NewPassword))
        ).ToLowerInvariant();

        user.PasswordHash = newHash;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin")]
    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> AdminResetPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { ok = false, error = "User not found" });

        var err = ValidatePasswordComplexity(request.NewPassword);
        if (err != null)
            return BadRequest(new { ok = false, error = err });

        var newHash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.NewPassword))
        ).ToLowerInvariant();

        user.PasswordHash = newHash;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    private static string? ValidatePasswordComplexity(string password)
    {
        if (password.Length < 8)
            return "Password must be at least 8 characters";
        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter";
        if (!password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter";
        if (!password.Any(char.IsDigit))
            return "Password must contain at least one number";
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return "Password must contain at least one special character";
        return null;
    }
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Role { get; set; }
    public string? DisplayName { get; set; }
    public string? CustomerName { get; set; }
}

public class UpdateUserRequest
{
    public string? Role { get; set; }
    public string? DisplayName { get; set; }
    public string? CustomerName { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}
