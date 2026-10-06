using System.Text;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Tenancy;

public enum TenantStatus
{
    Active,
    Suspended,
    Archived,
}

/// <summary>A customer organisation (D7). Not tenant-owned: it is the tenant.</summary>
public sealed class Tenant : AggregateRoot
{
    public const int NameMaxLength = 200;
    public const int CodeMaxLength = 32;
    public const string DefaultTimeZone = "Africa/Cairo";

    private Tenant()
    {
        Name = string.Empty;
        Code = string.Empty;
        Country = string.Empty;
        City = string.Empty;
        TimeZone = DefaultTimeZone;
    }

    public string Name { get; private set; }
    public string Code { get; private set; }
    public TenantStatus Status { get; private set; }
    public Guid? LicensingCustomerId { get; private set; }
    public string Country { get; private set; }
    public string City { get; private set; }
    public string TimeZone { get; private set; }
    public DateOnly CustomerSince { get; private set; }
    public Guid? LogoMediaId { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status == TenantStatus.Active;

    public static Tenant Create(
        string name, string? code, string country, string city, string? timeZone, DateOnly customerSince, Guid? licensingCustomerId, DateTimeOffset now, Guid? id = null)
    {
        var tenant = new Tenant
        {
            Name = Guard.NotEmpty(name, nameof(Name), NameMaxLength),
            Country = Guard.NotEmpty(country, nameof(Country), 100),
            City = Guard.NotEmpty(city, nameof(City), 100),
            TimeZone = Guard.MaxLength(timeZone, nameof(TimeZone), 64) ?? DefaultTimeZone,
            CustomerSince = customerSince,
            LicensingCustomerId = licensingCustomerId,
            CreatedAt = now,
            Status = TenantStatus.Active,
        };
        if (id is { } fixedId)
            tenant.Id = Guard.NotEmpty(fixedId, nameof(Id));
        tenant.Code = NormalizeCode(code ?? CodeFromName(tenant.Name));
        tenant.Raise(new TenantCreatedV1(tenant.Id, tenant.TimeZone, now));
        return tenant;
    }

    public static string NormalizeCode(string code)
    {
        var normalized = Guard.NotEmpty(code, nameof(Code), CodeMaxLength).ToUpperInvariant();
        Guard.Against(!normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            Error.Validation(Guard.ValidationCode, "Code may contain letters, digits, '-' and '_' only."));
        return normalized;
    }

    /// <summary>"Acme Corporation" -> "ACME-CORPORATION" (trimmed to 32 characters).</summary>
    public static string CodeFromName(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var code = builder.ToString().Trim('-');
        code = code.Length > CodeMaxLength ? code[..CodeMaxLength].TrimEnd('-') : code;
        return code.Length == 0 ? "CUSTOMER" : code;
    }

    public void Update(string name, string country, string city, string timeZone)
    {
        Guard.Against(Status == TenantStatus.Archived, TenancyErrors.InvalidTransition);
        Name = Guard.NotEmpty(name, nameof(Name), NameMaxLength);
        Country = Guard.NotEmpty(country, nameof(Country), 100);
        City = Guard.NotEmpty(city, nameof(City), 100);
        TimeZone = Guard.NotEmpty(timeZone, nameof(TimeZone), 64);
    }

    public void LinkLicensingCustomer(Guid licensingCustomerId) =>
        LicensingCustomerId = Guard.NotEmpty(licensingCustomerId, nameof(LicensingCustomerId));

    /// <summary>Users cannot sign in; devices stay connected but the portal is closed.</summary>
    public void Suspend(string reason, DateTimeOffset now)
    {
        Guard.Against(Status != TenantStatus.Active, TenancyErrors.InvalidTransition);
        Status = TenantStatus.Suspended;
        SuspendedAt = now;
        SuspensionReason = Guard.NotEmpty(reason, "Reason", 500);
        Raise(new TenantSuspendedV1(Id, SuspensionReason, now));
    }

    public void Resume(DateTimeOffset now)
    {
        Guard.Against(Status != TenantStatus.Suspended, TenancyErrors.InvalidTransition);
        Status = TenantStatus.Active;
        SuspendedAt = null;
        SuspensionReason = null;
        Raise(new TenantResumedV1(Id, now));
    }

    /// <summary>Terminal for the UI: hidden from default lists, devices disconnected (<c>TENANT_ARCHIVED</c>).</summary>
    public void Archive(string reason, DateTimeOffset now)
    {
        Guard.Against(Status == TenantStatus.Archived, TenancyErrors.InvalidTransition);
        Status = TenantStatus.Archived;
        ArchivedAt = now;
        SuspensionReason = Guard.NotEmpty(reason, "Reason", 500);
        Raise(new TenantArchivedV1(Id, SuspensionReason, now));
    }
}

public sealed record TenantCreatedV1(Guid TenantId, string TimeZone, DateTimeOffset At) : DomainEvent(At);

public sealed record TenantSuspendedV1(Guid TenantId, string Reason, DateTimeOffset At) : DomainEvent(At);

public sealed record TenantResumedV1(Guid TenantId, DateTimeOffset At) : DomainEvent(At);

public sealed record TenantArchivedV1(Guid TenantId, string Reason, DateTimeOffset At) : DomainEvent(At);
