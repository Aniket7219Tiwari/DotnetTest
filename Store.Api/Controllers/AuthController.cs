using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Store.Api.Application;
using Store.Api.Domain;
using Store.Api.Infrastructure;

namespace Store.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(StoreDbContext db, TokenService tokens, ILogger<AuthController> logger)
    : ControllerBase
{
    private readonly PasswordHasher<User> _hasher = new();

    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 200 ||
            !System.Net.Mail.MailAddress.TryCreate(request.Email, out _) ||
            request.Password.Length < 8 || request.Password.Length > 128)
            return BadRequest(new ProblemDetails { Status = 400, Detail = "Provide a name, valid email and password of 8-128 characters." });
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            return Conflict(new ProblemDetails { Status = 409, Detail = "An account with this email already exists." });
        var user = new User { FullName = request.FullName.Trim(), Email = email, Role = UserRole.Customer };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            return Conflict(new ProblemDetails { Status = 409, Detail = "An account with this email already exists." });
        }
        var response = await IssueAsync(user, ct);
        return Created("/api/auth/login", response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Failed login attempt for {Email} from {RemoteIp}", email, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new ProblemDetails { Status = 401, Detail = "Email or password is incorrect." });
        }
        return Ok(await IssueAsync(user, ct));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var saved = await db.RefreshTokens.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (saved is null || saved.RevokedAt.HasValue || saved.ExpiresAt <= DateTime.UtcNow)
            return Unauthorized(new ProblemDetails { Status = 401, Detail = "Refresh token is invalid or expired." });
        saved.RevokedAt = DateTime.UtcNow;
        var response = tokens.Create(saved.User);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = saved.UserId,
            TokenHash = tokens.HashRefreshToken(response.RefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Unauthorized(new ProblemDetails { Status = 401, Detail = "Refresh token has already been used." });
        }
        return Ok(response);
    }

    private async Task<TokenResponse> IssueAsync(User user, CancellationToken ct)
    {
        var response = tokens.Create(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashRefreshToken(response.RefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync(ct);
        return response;
    }
}
