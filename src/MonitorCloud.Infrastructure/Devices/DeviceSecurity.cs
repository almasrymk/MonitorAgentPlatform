using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Devices;
using MonitorCloud.Infrastructure.Options;

namespace MonitorCloud.Infrastructure.Devices;

/// <summary>
/// 256-bit device secrets; the stored value is HMAC-SHA256 keyed with a pepper derived from the device key (D11).
/// Secrets hashed with a previous device key still verify (key rotation); <see cref="Hash"/> always uses the current key.
/// </summary>
internal sealed class DeviceSecretService(IOptions<JwtOptions> options) : IDeviceSecretService
{
    private readonly byte[] _pepper = Pepper(options.Value.DeviceSigningKey);
    private readonly byte[][] _previous = [.. options.Value.PreviousDeviceSigningKeys.Where(k => !string.IsNullOrEmpty(k)).Select(Pepper)];

    private static byte[] Pepper(string key) => SHA256.HashData(Encoding.UTF8.GetBytes($"device-secret:{key}"));

    public DeviceSecret NewSecret()
    {
        var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new DeviceSecret(secret, Hash(secret));
    }

    public string Hash(string secret) => Hash(_pepper, secret);

    private static string Hash(byte[] pepper, string secret) => Convert.ToHexString(HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(secret ?? string.Empty)));

    public bool Verify(string secret, string hash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(hash))
            return false;
        var expected = Encoding.ASCII.GetBytes(hash);
        var matched = false;
        foreach (var pepper in _previous.Prepend(_pepper))
            matched |= CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(Hash(pepper, secret)));
        return matched;
    }
}

/// <summary>Device access tokens (03 section 6): HS256 with the device key, audience <c>monitor-agent-gateway</c>.</summary>
internal sealed class DeviceTokenService(IOptions<JwtOptions> options) : IDeviceTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public DeviceAccessToken CreateDeviceToken(Guid deviceId, Guid tenantId, string fingerprint, DateTimeOffset now)
    {
        var expires = now.AddMinutes(_options.DeviceTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.DeviceAudience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", deviceId.ToString()),
                new Claim("tid", tenantId.ToString()),
                new Claim("typ", "device"),
                new Claim("fp", fingerprint),
                new Claim("jti", Guid.CreateVersion7().ToString("N")),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.DeviceSigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new DeviceAccessToken(_handler.CreateToken(descriptor), expires);
    }
}
