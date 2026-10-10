namespace MonitorCloud.Application.Abstractions.Commands;

/// <summary>
/// Signs remote actions with the cloud's ES256 key (<c>Commands:SigningKey</c>, 05 section 9). Agents receive the public
/// keys at enrollment (<c>commandSigningKeys</c>) and verify every command with the key named by <see cref="KeyId"/>.
/// </summary>
public interface ICommandSigner
{
    /// <summary>The <c>kid</c> of the current key.</summary>
    string KeyId { get; }

    /// <summary>ES256 (P-256, SHA-256) signature of the UTF-8 text, in IEEE P1363 form (64 bytes, r then s).</summary>
    byte[] Sign(string payload);

    /// <summary>The public keys as a JWK set: <c>{ "keys": [ { "kty": "EC", "crv": "P-256", "kid", "x", "y", "use": "sig", "alg": "ES256" } ] }</c>.</summary>
    string PublicKeysJson();
}
