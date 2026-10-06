using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Infrastructure.Options;

namespace MonitorCloud.Infrastructure.Identity;

/// <summary>HS256 access tokens with the claims of 03 section 1, and HMAC-hashed refresh/invitation secrets.</summary>
internal sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken CreateAccessToken(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("name", user.FullName),
            new("role", user.Role),
            new("typ", "user"),
            new("lang", user.PreferredLanguage),
            new("jti", Guid.CreateVersion7().ToString("N")),
        };
        if (user.TenantId is { } tenantId)
            claims.Add(new Claim("tid", tenantId.ToString()));
        if (user.LocationScope.Count > 0)
            claims.Add(new Claim("loc", string.Join(',', user.LocationScope)));
        claims.AddRange(Roles.PermissionsOf(user.Role).Select(p => new Claim("perm", p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    public SecretToken NewRefreshToken() => NewSecret("refresh");

    public string HashRefreshToken(string token) => Hash("refresh", token);

    public SecretToken NewInvitationToken() => NewSecret("invitation");

    public string HashInvitationToken(string token) => Hash("invitation", token);

    private SecretToken NewSecret(string purpose)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new SecretToken(token, Hash(purpose, token));
    }

    /// <summary>HMAC-SHA256 keyed with the signing key, separated by purpose.</summary>
    private string Hash(string purpose, string token)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{_options.SigningKey}"));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(token)));
    }
}
