using MonitorCloud.Infrastructure.Licensing.Fake;

namespace MonitorCloud.TestShared.Builders;

/// <summary>Explicit fake Licensing data for tests: the four plans of 08 and helpers to add customers.</summary>
public static class TestLicensing
{
    public static readonly (string Code, string Name, int Limit, string[] Features)[] Plans =
    [
        ("STARTER", "Starter", 50, ["monitoring", "alerts", "reports.basic"]),
        ("PROFESSIONAL", "Professional", 200, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints"]),
        ("BUSINESS", "Business", 250, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints", "reports.advanced", "archive"]),
        ("ENTERPRISE", "Enterprise", 500, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints", "reports.advanced", "archive", "notifications.webhook", "remote.actions"]),
    ];

    public static FakeLicensingData NewData() => new()
    {
        ProductCode = "000001",
        Plans = [.. Plans.Select((p, i) => new FakePlan { Id = Guid.CreateVersion7(), Code = p.Code, Name = p.Name, Features = [.. p.Features], MaxActivations = p.Limit, Price = 49 + (i * 100) })],
    };

    /// <summary>Adds a customer with one subscription and one licence; returns the licence.</summary>
    public static FakeLicense AddCustomer(
        this FakeLicensingData data, Guid customerId, string name, string plan, DateTimeOffset now, int renewsInDays = 200,
        string subscriptionStatus = "Active", string licenseStatus = "Active", int? maxActivations = -1, int devices = 0, string? productKey = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var subscriptionId = Guid.CreateVersion7();
        data.Customers.Add(new FakeCustomer { Id = customerId, Name = name, Country = "Egypt", CreatedAt = now.AddYears(-1) });
        data.Subscriptions.Add(new FakeSubscription { Id = subscriptionId, CustomerId = customerId, PlanCode = plan, Status = subscriptionStatus, StartDate = now.AddYears(-1), EndDate = now.AddDays(renewsInDays) });
        var license = new FakeLicense
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            LicenseNumber = $"LIC-{name.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()}-{data.Licenses.Count + 1:0000}",
            ProductKey = productKey ?? NewKey(),
            Status = licenseStatus,
            ExpiresAt = now.AddDays(renewsInDays),
            MaxActivations = maxActivations == -1 ? Plans.First(p => p.Code == plan).Limit : maxActivations,
            Devices = [.. Enumerable.Range(1, devices).Select(i => $"{name.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}-dev-{i:0000}")],
        };
        data.Licenses.Add(license);
        return license;
    }

    public static string NewKey()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(30);
        var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
        return string.Join('-', Enumerable.Range(0, 5).Select(g => new string(chars, g * 6, 6)));
    }
}
