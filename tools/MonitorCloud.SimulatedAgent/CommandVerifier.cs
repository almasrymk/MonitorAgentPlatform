using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.SimulatedAgent;

/// <summary>
/// The agent's checks of a remote action (05 section 9), in the order the real agent applies them: local switch, known
/// key, ES256 signature over <c>command_id|type|parameters_json|expires_at|nonce|device_id</c> (expiry as Unix
/// milliseconds), not expired and at most 5 minutes ahead, nonce not seen in the last 10 minutes.
/// </summary>
public sealed class CommandVerifier(Guid deviceId, TimeProvider clock)
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NonceMemory = TimeSpan.FromMinutes(10);

    /// <summary>Allowed clock difference between cloud and device.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);
    private Dictionary<string, ECDsa> _keys = new(StringComparer.Ordinal);

    /// <summary>The local switch (<c>Cloud:AllowRemoteActions</c>): off refuses every command.</summary>
    public bool AllowRemoteActions { get; set; } = true;

    /// <summary>Loads the <c>commandSigningKeys</c> JWK set received at enrollment.</summary>
    public void LoadKeys(string jwks)
    {
        var keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(jwks);
        foreach (var key in document.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (key.GetProperty("kty").GetString() != "EC" || key.GetProperty("crv").GetString() != "P-256")
                continue;
            var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = FromBase64Url(key.GetProperty("x").GetString()!), Y = FromBase64Url(key.GetProperty("y").GetString()!) },
            });
            keys[key.GetProperty("kid").GetString()!] = ecdsa;
        }

        _keys = keys;
    }

    /// <summary>Null when the command may run; otherwise why it is refused (sent back as <c>REJECTED</c> or <c>EXPIRED</c>).</summary>
    public (CommandStatus Status, string Reason)? Check(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!AllowRemoteActions)
            return (CommandStatus.Rejected, "Remote actions are turned off on this device.");
        if (!_keys.TryGetValue(command.KeyId, out var key))
            return (CommandStatus.Rejected, "Unknown signing key.");
        if (!Guid.TryParseExact(command.CommandId, "N", out var commandId) || command.ExpiresAt is null)
            return (CommandStatus.Rejected, "Malformed command.");
        var expiresAt = command.ExpiresAt.ToDateTimeOffset();
        var payload = string.Join('|', commandId.ToString("N"), command.Type, command.ParametersJson,
            expiresAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), command.Nonce, deviceId.ToString("N"));
        // The device id is part of the signed text: a command signed for another device fails here.
        if (!key.VerifyData(Encoding.UTF8.GetBytes(payload), command.Signature.ToByteArray(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return (CommandStatus.Rejected, "Invalid signature.");
        var now = clock.GetUtcNow();
        if (expiresAt <= now)
            return (CommandStatus.Expired, "The command has expired.");
        if (expiresAt > now + MaxLifetime + Skew)
            return (CommandStatus.Rejected, "The expiry is more than 5 minutes ahead.");
        foreach (var (nonce, at) in _nonces)
        {
            if (now - at > NonceMemory)
                _nonces.TryRemove(nonce, out _);
        }

        if (!_nonces.TryAdd(command.Nonce, now))
            return (CommandStatus.Rejected, "Replayed nonce.");
        return null;
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
