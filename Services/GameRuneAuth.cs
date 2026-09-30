using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GameListerBackend.Data;
using Microsoft.IdentityModel.Tokens;

namespace GameListerBackend.Services;

public static class GameRuneAuth
{
    private const int Iterations = 100_000;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split('.', 3);
            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static string GenerateJwt(AppUser user, IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("JWT_KEY")
            ?? throw new InvalidOperationException("JWT key is not configured. Set Jwt:Key or JWT_KEY (min 32 chars).");
        var issuer = configuration["Jwt:Issuer"] ?? "gamerune";
        var audience = configuration["Jwt:Audience"] ?? "gamerune-frontend";
        var expiryMinutes = configuration.GetValue("Jwt:ExpiryMinutes", 1440);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
            ]),
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256Signature),
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public static Guid GetUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Missing sub claim.");
        return Guid.Parse(sub);
    }
}
