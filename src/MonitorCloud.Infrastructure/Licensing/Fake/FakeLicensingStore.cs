using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Licensing.Contracts;

namespace MonitorCloud.Infrastructure.Licensing.Fake;

/// <summary>
/// In-memory Licensing Platform for Development and tests (04 section 2). Enforces limits and statuses with the
/// Licensing error codes and signs licence tokens with a development ES256 key. Test hooks change plans and
/// statuses, append change events and simulate outages or rate limiting.
/// </summary>
public sealed class FakeLicensingStore
{
    public const string Issuer = "licensing-platform";
    public static readonly Guid VendorTenantId = new("00000000-0000-7000-8000-00000000f00d");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly List<ChangeItem> _changes = [];
    private readonly Dictionary<string, SeatActivation> _idempotent = new(StringComparer.Ordinal);
    private FakeLicensingData _data = new();

    public string Kid { get; } = "fake-dev-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    /// <summary>When true every call fails with <c>LICENSING_UNAVAILABLE</c> (outage simulation).</summary>
    public bool Unavailable { get; set; }

    /// <summary>The next N calls answer <c>RATE_LIMITED</c>.</summary>
    public int RateLimitNextCalls { get; set; }

    public string ProductCode => _data.ProductCode;

    public FakeLicensingData Snapshot()
    {
        lock (_gate)
            return JsonSerializer.Deserialize<FakeLicensingData>(JsonSerializer.Serialize(_data, Json), Json)!;
    }

    public void Load(FakeLicensingData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            _data = data;
            _changes.Clear();
            _idempotent.Clear();
        }
    }

    public void LoadFile(string path)
    {
        if (File.Exists(path))
            Load(JsonSerializer.Deserialize<FakeLicensingData>(File.ReadAllText(path), Json) ?? new FakeLicensingData());
    }

    public static void Save(FakeLicensingData data, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(data, Json));
    }

    // ------------------------------------------------------------------ test hooks

    public void Mutate(Action<FakeLicensingData> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
            change(_data);
    }

    /// <summary>Moves the customer's usable subscription to another plan and records the change event.</summary>
    public void ChangePlan(Guid customerId, string planCode, DateTimeOffset at)
    {
        lock (_gate)
        {
            var subscription = _data.Subscriptions.First(s => s.CustomerId == customerId && s.Status is "Active" or "Trial");
            subscription.PlanCode = planCode;
            var plan = _data.Plans.First(p => p.Code == planCode);
            foreach (var license in _data.Licenses.Where(l => l.SubscriptionId == subscription.Id))
                license.MaxActivations = plan.MaxActivations;
            Append("SubscriptionPlanChangedV1", at, customerId, subscription.Id, null);
        }
    }

    public void SetSubscriptionStatus(Guid customerId, string status, DateTimeOffset at)
    {
        lock (_gate)
        {
            var subscription = _data.Subscriptions.Where(s => s.CustomerId == customerId).OrderByDescending(s => s.EndDate).First();
            subscription.Status = status;
            Append($"Subscription{status}V1", at, customerId, subscription.Id, null);
        }
    }

    public void SetLicenseStatus(Guid licenseId, string status, DateTimeOffset at)
    {
        lock (_gate)
        {
            var license = _data.Licenses.First(l => l.Id == licenseId);
            license.Status = status;
            Append("LicenseStatusChangedV1", at, license.CustomerId, license.SubscriptionId, license.Id);
        }
    }

    public void Append(string type, DateTimeOffset at, Guid? customerId, Guid? subscriptionId, Guid? licenseId)
    {
        lock (_gate)
            _changes.Add(new ChangeItem(Guid.CreateVersion7(), type, at, customerId, subscriptionId, licenseId));
    }

    // ------------------------------------------------------------------ operations used by the gateway

    internal T Read<T>(Func<FakeLicensingData, IReadOnlyList<ChangeItem>, T> read)
    {
        lock (_gate)
            return read(_data, _changes);
    }

    internal T Write<T>(Func<FakeLicensingData, Dictionary<string, SeatActivation>, T> write)
    {
        lock (_gate)
            return write(_data, _idempotent);
    }

    internal string Sign(FakeLicense license, string deviceId, IReadOnlyList<string> features, string planCode, DateTimeOffset now, DateTimeOffset checkAfter, DateTimeOffset offlineUntil)
    {
        var claims = new List<Claim>
        {
            new("sub", deviceId),
            new("lic", license.LicenseNumber),
            new("lid", license.Id.ToString()),
            new("tid", VendorTenantId.ToString()),
            new("cid", license.CustomerId.ToString()),
            new("product", ProductCode),
            new("plan", planCode),
            new("features", JsonSerializer.Serialize(features), JsonClaimValueTypes.JsonArray),
            new("check_after", checkAfter.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
        };
        if (license.ExpiresAt is { } exp)
            claims.Add(new Claim("lic_exp", exp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ProductCode,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = offlineUntil.UtcDateTime,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_key) { KeyId = Kid }, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>Same shape as the Licensing Platform's <c>GET /api/v1/signing-keys</c> (JWKS plus PEM list).</summary>
    internal string SigningKeysJson()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var pem = PemEncoding.WriteString("PUBLIC KEY", _key.ExportSubjectPublicKeyInfo());
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "EC", crv = "P-256", x = Base64UrlEncoder.Encode(parameters.Q.X), y = Base64UrlEncoder.Encode(parameters.Q.Y), kid = Kid, alg = "ES256", use = "sig", status = "active" },
            },
            pem = new[] { new { kid = Kid, status = "active", publicKeyPem = pem } },
        }, Json);
    }
}
