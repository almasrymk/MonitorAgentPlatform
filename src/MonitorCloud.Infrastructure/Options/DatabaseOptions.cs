using System.ComponentModel.DataAnnotations;

namespace MonitorCloud.Infrastructure.Options;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Development applies migrations at start-up; other environments run them as a release step.</summary>
    public bool ApplyMigrations { get; set; }

    public bool DemoData { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "monitor-cloud";

    [Required]
    public string Audience { get; set; } = "monitor-cloud-portal";

    [Required]
    public string DeviceAudience { get; set; } = "monitor-agent-gateway";

    /// <summary>HS256 key for user access tokens (at least 32 bytes). From the environment outside Development.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>HS256 key for device tokens; must differ from <see cref="SigningKey"/>.</summary>
    [Required]
    public string DeviceSigningKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    [Range(5, 240)]
    public int DeviceTokenMinutes { get; set; } = 60;
}
