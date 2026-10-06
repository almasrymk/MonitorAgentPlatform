using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Tenancy;

public enum LocationStatus
{
    Active,
    Closed,
}

/// <summary>A physical place of a customer (D8). Each tenant has exactly one default location, "Unassigned".</summary>
public sealed class Location : AggregateRoot, ILocationAggregate
{
    public const string DefaultName = "Unassigned";
    public const string DefaultCode = "UNASSIGNED";

    private Location()
    {
        Name = string.Empty;
        Code = string.Empty;
        TimeZone = Tenant.DefaultTimeZone;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? AddressLine { get; private set; }
    public string TimeZone { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public Guid? ImageMediaId { get; private set; }
    public bool IsDefault { get; private set; }
    public LocationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Location CreateDefault(Guid tenantId, string timeZone, DateTimeOffset now) =>
        new()
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            Name = DefaultName,
            Code = DefaultCode,
            TimeZone = Guard.NotEmpty(timeZone, nameof(TimeZone), 64),
            IsDefault = true,
            CreatedAt = now,
        };

    public static Location Create(Guid tenantId, LocationDetails details, DateTimeOffset now, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        var location = new Location { TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), CreatedAt = now };
        if (id is { } fixedId)
            location.Id = Guard.NotEmpty(fixedId, nameof(Id));
        location.Apply(details);
        return location;
    }

    public void Update(LocationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Apply(details);
    }

    public void Close()
    {
        Guard.Against(IsDefault, TenancyErrors.LocationIsDefault);
        Status = LocationStatus.Closed;
    }

    public void Reopen() => Status = LocationStatus.Active;

    /// <summary>Checks the rules that the domain can see; the device count is checked by the handler.</summary>
    public void EnsureCanBeDeleted() => Guard.Against(IsDefault, TenancyErrors.LocationIsDefault);

    private void Apply(LocationDetails details)
    {
        Name = Guard.NotEmpty(details.Name, nameof(Name), 200);
        // The default location keeps its code so devices without a location code always land there.
        if (!IsDefault)
            Code = Tenant.NormalizeCode(details.Code);
        City = Guard.MaxLength(details.City, nameof(City), 100);
        Country = Guard.MaxLength(details.Country, nameof(Country), 100);
        AddressLine = Guard.MaxLength(details.AddressLine, nameof(AddressLine), 300);
        TimeZone = Guard.NotEmpty(details.TimeZone, nameof(TimeZone), 64);
        ContactName = Guard.MaxLength(details.ContactName, nameof(ContactName), 200);
        ContactEmail = Guard.MaxLength(details.ContactEmail, nameof(ContactEmail), 256);
        ContactPhone = Guard.MaxLength(details.ContactPhone, nameof(ContactPhone), 50);
    }
}

public sealed record LocationDetails(
    string Name,
    string Code,
    string? City,
    string? Country,
    string? AddressLine,
    string TimeZone,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone);
