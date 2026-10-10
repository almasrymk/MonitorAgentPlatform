using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Abstractions.Commands;
using MonitorCloud.Infrastructure.Storage;

namespace MonitorCloud.Infrastructure.Commands;

/// <summary>The <c>Commands</c> section (05 section 9).</summary>
public sealed class CommandOptions
{
    public const string Section = "Commands";

    /// <summary>
    /// The ES256 private key as PKCS#8 PEM (<c>-----BEGIN PRIVATE KEY-----</c>), from the environment in production.
    /// Outside Production an empty value creates a key once and keeps it in <c>{Storage:Root}/keys/command-signing.pem</c>.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Public keys of replaced signing keys (PEM, <c>-----BEGIN PUBLIC KEY-----</c>), still published to agents after a rotation.</summary>
    public string[] PreviousPublicKeys { get; set; } = [];

    /// <summary>Commands are expired every 30 s; off with the background jobs in tests.</summary>
    public bool JobsEnabled { get; set; } = true;
}

/// <summary>ES256 over the canonical payload (P1363, 64 bytes); <c>kid</c> = the RFC 7638 thumbprint of the public key.</summary>
internal sealed class EcdsaCommandSigner : ICommandSigner, IDisposable
{
    private readonly ECDsa _key;
    private readonly string _publicKeys;

    public EcdsaCommandSigner(IOptions<CommandOptions> options, IOptions<StorageOptions> storage, IHostEnvironment environment)
    {
        _key = ECDsa.Create();
        var pem = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(pem))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("Commands:SigningKey is empty.");
            pem = DevelopmentKey(StorageRoot.Of(storage, environment));
        }

        _key.ImportFromPem(pem);
        if (_key.KeySize != 256)
            throw new InvalidOperationException("Commands:SigningKey must be a P-256 (ES256) key.");
        var current = Jwk(_key);
        KeyId = current.Kid;
        var keys = new List<Dictionary<string, string>> { current.Json };
        foreach (var previous in options.Value.PreviousPublicKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            using var old = ECDsa.Create();
            old.ImportFromPem(previous);
            keys.Add(Jwk(old).Json);
        }

        _publicKeys = JsonSerializer.Serialize(new { keys });
    }

    public string KeyId { get; }

    public byte[] Sign(string payload) => _key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public string PublicKeysJson() => _publicKeys;

    public void Dispose() => _key.Dispose();

    private static (string Kid, Dictionary<string, string> Json) Jwk(ECDsa key)
    {
        var p = key.ExportParameters(false);
        var x = Base64UrlEncoder.Encode(p.Q.X);
        var y = Base64UrlEncoder.Encode(p.Q.Y);
        // RFC 7638: SHA-256 of the required members in lexicographic order, without spaces.
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}")));
        return (kid, new Dictionary<string, string> { ["kty"] = "EC", ["crv"] = "P-256", ["kid"] = kid, ["x"] = x, ["y"] = y, ["use"] = "sig", ["alg"] = "ES256" });
    }

    /// <summary>A key for development and tests, created once so enrolled agents keep verifying after a restart.</summary>
    private static string DevelopmentKey(string root)
    {
        var path = Path.Combine(root, "keys", "command-signing.pem");
        if (File.Exists(path))
            return File.ReadAllText(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(path, pem);
        return pem;
    }
}
