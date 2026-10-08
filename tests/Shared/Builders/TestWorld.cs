using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Identity;
using MonitorCloud.Application.Devices;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.TestShared.Builders;

/// <summary>One customer of the test world: its locations and one user per role.</summary>
/// <summary>
/// Device1 is in Location1, Device2 in Location2 (both online, healthy, licensed); EnrollmentCodeId belongs to Location1.
/// Alert1 is an open Info alert of Device1 (Info does not change the health); Recipient1 receives every alert e-mail.
/// </summary>
public sealed record TestTenant(
    Tenant Tenant, Location DefaultLocation, Location Location1, Location Location2, IReadOnlyDictionary<string, User> Users, User RestrictedManager,
    Device Device1, Device Device2, Guid EnrollmentCodeId, Alert Alert1, AlertRecipient Recipient1)
{
    public Guid Id => Tenant.Id;
    public User Administrator => Users[Roles.Administrator];
    public User ItManager => Users[Roles.ITManager];
    public User Technician => Users[Roles.Technician];
    public User ReportViewer => Users[Roles.ReportViewer];
}

/// <summary>
/// Two customers (A and B) and the two platform users, built directly in the database with explicit values
/// (no demo seed). Every user has the password <see cref="Password"/>.
/// </summary>
public sealed record TestWorld(User PlatformAdmin, User PlatformSupport, TestTenant A, TestTenant B)
{
    public const string Password = "Test-Pass#2026";

    /// <summary>The Licensing customer of a test tenant (A: Enterprise with 3 seats, B: Starter with 1 seat).</summary>
    public Guid LicensingCustomerOf(TestTenant tenant) => tenant.Tenant.LicensingCustomerId!.Value;

    public User UserOf(string role, TestTenant tenant) => role switch
    {
        Roles.PlatformAdmin => PlatformAdmin,
        Roles.PlatformSupport => PlatformSupport,
        _ => tenant.Users[role],
    };

    public static async Task<TestWorld> CreateAsync(TestApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var now = app.Clock.GetUtcNow();
        var hash = app.Services.GetRequiredService<IPasswordHasher>().Hash(Password);

        return await app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var admin = User.CreatePlatformUser("platform.admin@monitor.local", "Platform Admin", Roles.PlatformAdmin, hash, now);
            var support = User.CreatePlatformUser("platform.support@monitor.local", "Platform Support", Roles.PlatformSupport, hash, now);
            db.AddRange(admin, support);

            var secrets = sp.GetRequiredService<IDeviceSecretService>();
            var a = AddTenant(db, secrets, "Alpha Test Company", "ALPHA", "alpha.test", hash, now);
            var b = AddTenant(db, secrets, "Beta Test Company", "BETA", "beta.test", hash, now);
            await db.SaveChangesAsync();

            // The fake Licensing Platform knows both customers; the sync fills their entitlements.
            var licensing = TestLicensing.NewData();
            licensing.AddCustomer(a.Tenant.LicensingCustomerId!.Value, a.Tenant.Name, "ENTERPRISE", now, devices: 3);
            licensing.AddCustomer(b.Tenant.LicensingCustomerId!.Value, b.Tenant.Name, "STARTER", now, devices: 1);
            sp.GetRequiredService<FakeLicensingStore>().Load(licensing);
            await sp.GetRequiredService<LicensingSyncService>().ReconcileAllAsync(CancellationToken.None);
            return new TestWorld(admin, support, a, b);
        });
    }

    /// <summary>The device secret of every test-world device.</summary>
    public const string DeviceSecret = "TEST-ONLY-device-secret";

    private static TestTenant AddTenant(AppDbContext db, IDeviceSecretService secrets, string name, string code, string domain, string hash, DateTimeOffset now)
    {
        var tenant = Tenant.Create(name, code, "Egypt", "Cairo", "Africa/Cairo", new DateOnly(2025, 1, 1), Guid.CreateVersion7(), now);
        tenant.ClearDomainEvents();
        var defaultLocation = Location.CreateDefault(tenant.Id, "Africa/Cairo", now);
        var location1 = Location.Create(tenant.Id, new LocationDetails($"{code} North", $"{code}-N", "Cairo", "Egypt", "1 Sample Street", "Africa/Cairo", null, null, null), now);
        var location2 = Location.Create(tenant.Id, new LocationDetails($"{code} South", $"{code}-S", "Giza", "Egypt", null, "Africa/Cairo", null, null, null), now);
        db.AddRange(tenant, defaultLocation, location1, location2);

        var users = Roles.Tenant.ToDictionary(
            role => role,
            role => User.CreateTenantUser(tenant.Id, $"{role.ToLowerInvariant()}@{domain}", $"{name} {role}", role, [], hash, now));
        var restricted = User.CreateTenantUser(tenant.Id, $"restricted@{domain}", $"{name} Restricted Manager", Roles.ITManager, [location1.Id], hash, now);
        db.AddRange(users.Values);
        db.Add(restricted);

        var device1 = AddDevice(db, secrets, tenant.Id, location1.Id, $"{code}-PC-01", now);
        var device2 = AddDevice(db, secrets, tenant.Id, location2.Id, $"{code}-PC-02", now);
        var enrollmentCode = LocationEnrollmentCode.Create(tenant.Id, location1.Id, EnrollmentCodes.Hash($"LOC-{code}-TEST"), EnrollmentCodes.Prefix($"LOC-{code}-TEST"), TimeSpan.FromDays(7), null, null, now);
        db.Add(enrollmentCode);
        var alert = Alert.Raise(tenant.Id, location1.Id, device1.Id, "disk-D", AlertCategories.Storage, AlertSeverity.Info, "Disk D: is 80% full", "Free space 20%", AlertSource.Agent, now, notify: false);
        alert.ClearDomainEvents();
        var recipient = AlertRecipient.Create(tenant.Id, "Operations", $"ops@{domain}", RecipientEvents.All, null);
        db.AddRange(alert, recipient);
        return new TestTenant(tenant, defaultLocation, location1, location2, users, restricted, device1, device2, enrollmentCode.Id, alert, recipient);
    }

    private static Device AddDevice(AppDbContext db, IDeviceSecretService secrets, Guid tenantId, Guid locationId, string name, DateTimeOffset now)
    {
        var info = new AgentInfo(name, OsFamily.Windows, "Windows 11 Pro", "10.0.26100", "x64", "1.1.0", 1, "10.0.0.10", "203.0.113.10", null);
        var device = Device.Enroll(tenantId, locationId, $"fp-{name.ToLowerInvariant()}", info, now);
        device.ClearDomainEvents();
        var state = DeviceState.Create(device.Id, tenantId, locationId, OsFamily.Windows, LicenseStateValue.Licensed, now);
        state.SeedAs(ConnectionState.Online, now, now.AddHours(-1), now, TimeSpan.FromDays(14));
        db.AddRange(device, DeviceCredential.Issue(device.Id, tenantId, secrets.Hash(DeviceSecret), now), state);
        return device;
    }
}
