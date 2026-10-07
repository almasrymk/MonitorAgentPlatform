namespace MonitorCloud.Infrastructure.Licensing.Fake;

/// <summary>The content of <c>seed/licensing-fake.json</c> (08 section 5).</summary>
public sealed class FakeLicensingData
{
    public string ProductCode { get; set; } = "000001";
    public List<FakePlan> Plans { get; set; } = [];
    public List<FakeCustomer> Customers { get; set; } = [];
    public List<FakeSubscription> Subscriptions { get; set; } = [];
    public List<FakeLicense> Licenses { get; set; } = [];
}

public sealed class FakePlan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public List<string> Features { get; set; } = [];
    public int? MaxActivations { get; set; }
    public int? DurationDays { get; set; } = 365;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed class FakeCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Country { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FakeSubscription
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
}

public sealed class FakeLicense
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string ProductKey { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxActivations { get; set; }

    /// <summary>Device ids (fingerprints) holding a seat.</summary>
    public List<string> Devices { get; set; } = [];
}
