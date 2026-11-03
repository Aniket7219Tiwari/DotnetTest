using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Store.Api.Application;
using Store.Api.Domain;

namespace Store.Api.Infrastructure;

public sealed class TokenService(IConfiguration configuration)
{
    public TokenResponse Create(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(15);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var signingKey = GetKey();
        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims,
            expires: expires, signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), expires, user.Role.ToString());
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public SymmetricSecurityKey GetKey() =>
        new(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key must be configured.")));
}
