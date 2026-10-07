namespace MonitorCloud.Application.Devices;

public sealed record DeviceSecret(string Secret, string Hash);

/// <summary>256-bit device secrets stored as HMAC-SHA256 with the server pepper (D11).</summary>
public interface IDeviceSecretService
{
    DeviceSecret NewSecret();

    string Hash(string secret);

    /// <summary>Constant-time comparison of a presented secret with the stored hash.</summary>
    bool Verify(string secret, string hash);
}

public sealed record DeviceAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Device JWTs (03 section 6): own key, audience <c>monitor-agent-gateway</c>, 60 minutes.</summary>
public interface IDeviceTokenService
{
    DeviceAccessToken CreateDeviceToken(Guid deviceId, Guid tenantId, string fingerprint, DateTimeOffset now);
}
