using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Archive;

/// <summary>Company details of a customer (02 section 10).</summary>
public sealed class CustomerProfile : Entity, ITenantOwned
{
    private CustomerProfile()
    {
    }

    public Guid TenantId { get; private set; }
    public string? Industry { get; private set; }
    public string? Website { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? AccountManager { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CustomerProfile Create(Guid tenantId, DateTimeOffset now) => new() { Id = tenantId, TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), UpdatedAt = now };

    public void Update(string? industry, string? website, string? phone, string? address, string? accountManager, DateTimeOffset now)
    {
        Industry = Guard.MaxLength(industry, nameof(Industry), 100);
        Website = Guard.MaxLength(website, nameof(Website), 200);
        Phone = Guard.MaxLength(phone, nameof(Phone), 50);
        Address = Guard.MaxLength(address, nameof(Address), 300);
        AccountManager = Guard.MaxLength(accountManager, nameof(AccountManager), 200);
        UpdatedAt = now;
    }
}

public sealed class ArchiveContact : Entity, ITenantOwned
{
    private ArchiveContact()
    {
        Name = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? JobTitle { get; private set; }
    public bool IsPrimary { get; private set; }

    public static ArchiveContact Create(Guid tenantId, string name, string? email, string? phone, string? jobTitle, bool isPrimary)
    {
        var contact = new ArchiveContact { TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)) };
        contact.Update(name, email, phone, jobTitle, isPrimary);
        return contact;
    }

    public void Update(string name, string? email, string? phone, string? jobTitle, bool isPrimary)
    {
        Name = Guard.NotEmpty(name, nameof(Name), 200);
        Email = Guard.MaxLength(email?.ToLowerInvariant(), nameof(Email), 256);
        Phone = Guard.MaxLength(phone, nameof(Phone), 50);
        JobTitle = Guard.MaxLength(jobTitle, nameof(JobTitle), 100);
        IsPrimary = isPrimary;
    }
}

/// <summary>A note; <see cref="IsInternal"/> notes are for platform staff only and never returned to customer roles.</summary>
public sealed class ArchiveNote : Entity, ITenantOwned
{
    private ArchiveNote()
    {
        Body = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public string Body { get; private set; }
    public bool IsInternal { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string AuthorName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static ArchiveNote Create(Guid tenantId, string body, bool isInternal, Guid authorUserId, string authorName, DateTimeOffset now) => new()
    {
        TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
        Body = Guard.NotEmpty(body, nameof(Body), 4000),
        IsInternal = isInternal,
        AuthorUserId = authorUserId,
        AuthorName = Guard.MaxLength(authorName, nameof(AuthorName), 200) ?? string.Empty,
        CreatedAt = now,
    };
}

/// <summary>An attachment of the customer archive; <see cref="IsInternal"/> files are for platform staff only.</summary>
public sealed class ArchiveFile : Entity, ITenantOwned
{
    private ArchiveFile()
    {
        DisplayName = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid MediaId { get; private set; }
    public string DisplayName { get; private set; }
    public long SizeBytes { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public bool IsInternal { get; private set; }

    public static ArchiveFile Create(Guid tenantId, Guid mediaId, string displayName, long sizeBytes, Guid userId, bool isInternal, DateTimeOffset now) => new()
    {
        TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
        MediaId = Guard.NotEmpty(mediaId, nameof(MediaId)),
        DisplayName = Guard.NotEmpty(displayName, nameof(DisplayName), 200),
        SizeBytes = sizeBytes,
        UploadedByUserId = userId,
        UploadedAt = now,
        IsInternal = isInternal,
    };
}

/// <summary>Remote access details for support (platform roles only). The values are stored encrypted (ASP.NET Data Protection).</summary>
public sealed class RemoteAccessEntry : Entity, ITenantOwned
{
    private RemoteAccessEntry()
    {
        Tool = string.Empty;
        Label = string.Empty;
        IdentifierProtected = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid? LocationId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string Tool { get; private set; }
    public string Label { get; private set; }
    public string IdentifierProtected { get; private set; }
    public string? PasswordProtected { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RemoteAccessEntry Create(Guid tenantId, Guid? locationId, Guid? deviceId, string tool, string label, string identifierProtected, string? passwordProtected, DateTimeOffset now)
    {
        var entry = new RemoteAccessEntry { TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), CreatedAt = now };
        entry.Update(locationId, deviceId, tool, label, identifierProtected, passwordProtected);
        return entry;
    }

    public void Update(Guid? locationId, Guid? deviceId, string tool, string label, string identifierProtected, string? passwordProtected)
    {
        LocationId = locationId;
        DeviceId = deviceId;
        Tool = Guard.NotEmpty(tool, nameof(Tool), 32);
        Label = Guard.NotEmpty(label, nameof(Label), 200);
        IdentifierProtected = Guard.NotEmpty(identifierProtected, nameof(IdentifierProtected), 4000);
        PasswordProtected = Guard.MaxLength(passwordProtected, nameof(PasswordProtected), 4000);
    }
}
