using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Infrastructure.Commands;
using MonitorCloud.Infrastructure.Storage;
using NSubstitute;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class CommandSignerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "monitorcloud-unit", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private EcdsaCommandSigner Signer(string key = "", string[]? previous = null, string environment = "Development")
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        host.ContentRootPath.Returns(Path.GetTempPath());
        return new EcdsaCommandSigner(
            Microsoft.Extensions.Options.Options.Create(new CommandOptions { SigningKey = key, PreviousPublicKeys = previous ?? [] }),
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { Root = _root }), host);
    }

    private static ECDsa FromJwk(JsonElement jwk) => ECDsa.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = Base64UrlEncoder.DecodeBytes(jwk.GetProperty("x").GetString()), Y = Base64UrlEncoder.DecodeBytes(jwk.GetProperty("y").GetString()) },
    });

    [Fact]
    public void A_signature_verifies_with_the_published_key()
    {
        using var signer = Signer();
        var jwk = JsonDocument.Parse(signer.PublicKeysJson()).RootElement.GetProperty("keys")[0];
        var signature = signer.Sign("payload");

        signature.Length.ShouldBe(64);
        jwk.GetProperty("kid").GetString().ShouldBe(signer.KeyId);
        using var key = FromJwk(jwk);
        key.VerifyData(Encoding.UTF8.GetBytes("payload"), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).ShouldBeTrue();
        key.VerifyData(Encoding.UTF8.GetBytes("payloaD"), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation).ShouldBeFalse();
    }

    [Fact]
    public void The_development_key_is_created_once_and_reused()
    {
        using var first = Signer();
        using var second = Signer();

        second.KeyId.ShouldBe(first.KeyId);
        File.Exists(Path.Combine(_root, "keys", "command-signing.pem")).ShouldBeTrue();
    }

    [Fact]
    public void A_configured_key_is_used_and_previous_public_keys_stay_published()
    {
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var old = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = Signer(current.ExportPkcs8PrivateKeyPem(), [old.ExportSubjectPublicKeyInfoPem()], "Production");

        var keys = JsonDocument.Parse(signer.PublicKeysJson()).RootElement.GetProperty("keys");
        keys.GetArrayLength().ShouldBe(2);
        keys[0].GetProperty("x").GetString().ShouldBe(Base64UrlEncoder.Encode(current.ExportParameters(false).Q.X));
        keys[1].GetProperty("x").GetString().ShouldBe(Base64UrlEncoder.Encode(old.ExportParameters(false).Q.X));
        signer.PublicKeysJson().ShouldNotContain("\"d\"");
    }

    [Fact]
    public void Production_refuses_an_empty_key_and_any_curve_but_P256()
    {
        Should.Throw<InvalidOperationException>(() => Signer(environment: "Production"));
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Should.Throw<InvalidOperationException>(() => Signer(p384.ExportPkcs8PrivateKeyPem()));
    }
}
