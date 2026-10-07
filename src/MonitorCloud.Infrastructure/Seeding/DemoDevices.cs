using System.Text.Json;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Devices;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Seeding;

/// <summary>
/// Part 2 of the demo seed (MC-305, 08 section 3): devices, their current state, licence rows and the inventory of
/// the eight fixed Cairo HQ devices. Deterministic for a given <see cref="Random"/>; dates are relative to now.
/// </summary>
internal sealed class DemoDevices(AppDbContext db, Random random, DateTimeOffset now, TimeSpan grace, FakeLicensingStore? signer)
{
    /// <summary>What one location holds. Healthy + Warning + Critical = Online; Online + Offline = Devices.</summary>
    public sealed record LocationPlan(int Devices, int Healthy, int Warning, int Critical, int Offline, int Unlicensed, int[]? OsCounts = null)
    {
        public int Online => Healthy + Warning + Critical;
    }

    private sealed record Fixed(
        string Name, OsFamily Os, string OsName, string OsVersion, string Ip, DeviceHealth Health, bool Online, bool Licensed, int Critical, int Warning, double OfflineHours = 0);

    /// <summary>08 section 3: Acme in detail. OS counts are Windows, Linux, macOS, Other.</summary>
    public static readonly LocationPlan[] Acme =
    [
        new(142, 126, 6, 2, 8, 4, [98, 28, 12, 4]),
        new(96, 77, 10, 4, 5, 9),
        new(78, 44, 20, 9, 5, 13),
    ];

    private static readonly Fixed[] CairoFixed =
    [
        new("WEB-SRV-01", OsFamily.Windows, "Windows Server 2019", "10.0.17763", "192.168.1.10", DeviceHealth.Critical, true, true, 1, 2),
        new("DB-SRV-01", OsFamily.Linux, "Ubuntu 22.04 LTS", "22.04", "192.168.1.20", DeviceHealth.Warning, true, true, 0, 1),
        new("APP-SRV-02", OsFamily.Windows, "Windows Server 2022", "10.0.20348", "192.168.1.30", DeviceHealth.Unknown, false, true, 1, 1, 6),
        new("DESK-01", OsFamily.MacOS, "macOS Sonoma 14.0", "14.0", "192.168.1.40", DeviceHealth.Healthy, true, true, 0, 0),
        new("BR-DC-01", OsFamily.Windows, "Windows Server 2019", "10.0.17763", "192.168.1.50", DeviceHealth.Warning, true, true, 0, 1),
        new("SQL-DB-01", OsFamily.Linux, "CentOS 7", "7.9.2009", "192.168.1.60", DeviceHealth.Critical, true, true, 1, 0),
        new("DEV-MAC-01", OsFamily.MacOS, "macOS Ventura 13.6", "13.6", "192.168.1.70", DeviceHealth.Warning, true, false, 0, 1),
        new("FILE-SRV-01", OsFamily.Windows, "Windows Server 2016", "10.0.14393", "192.168.1.80", DeviceHealth.Healthy, true, true, 0, 0),
    ];

    private static readonly string[] DnsServers = ["192.168.1.2", "192.168.1.3"];
    private static readonly string[] LocalUsers = ["Administrator", "svc-backup", "svc-iis", "operator", "auditor", "guest"];

    private static readonly OsFamily[] Families = [OsFamily.Windows, OsFamily.Linux, OsFamily.MacOS, OsFamily.Other];
    private static readonly int[] OsRatio = [69, 20, 8, 3];

    private static readonly Dictionary<OsFamily, (string Name, string Version)[]> OsNames = new()
    {
        [OsFamily.Windows] = [("Windows Server 2019", "10.0.17763"), ("Windows Server 2022", "10.0.20348"), ("Windows 11 Pro", "10.0.26100"), ("Windows 10 Pro", "10.0.19045"), ("Windows Server 2016", "10.0.14393")],
        [OsFamily.Linux] = [("Ubuntu 22.04 LTS", "22.04"), ("Ubuntu 24.04 LTS", "24.04"), ("Debian 12", "12.7"), ("Rocky Linux 9", "9.4"), ("CentOS 7", "7.9.2009")],
        [OsFamily.MacOS] = [("macOS Sonoma 14.5", "14.5"), ("macOS Ventura 13.6", "13.6"), ("macOS Sequoia 15.1", "15.1")],
        [OsFamily.Other] = [("FreeBSD 14.1", "14.1"), ("ChromeOS 126", "126.0")],
    };

    private static readonly Dictionary<OsFamily, string[]> Prefixes = new()
    {
        [OsFamily.Windows] = ["WEB-SRV", "APP-SRV", "DB-SRV", "FILE-SRV", "DC", "DESK", "LAP", "POS"],
        [OsFamily.Linux] = ["WEB-SRV", "APP-SRV", "DB-SRV"],
        [OsFamily.MacOS] = ["DEV-MAC", "LAP", "DESK"],
        [OsFamily.Other] = ["KIOSK", "POS"],
    };

    public int Created { get; private set; }

    /// <summary>A plan for a customer that is not described in detail: devices spread over the locations, about 93% online.</summary>
    public LocationPlan[] GenericPlan(int devices, int unlicensed, int locations)
    {
        var weights = Enumerable.Range(0, locations).Select(i => 1.0 / (i + 1)).ToArray();
        var perLocation = LargestRemainder(devices, weights);
        var unlicensedPerLocation = LargestRemainder(unlicensed, [.. perLocation.Select(n => (double)Math.Max(n, 0))]);
        return perLocation.Select((n, i) =>
        {
            var offline = n == 0 ? 0 : (int)Math.Round(n * (0.04 + random.NextDouble() * 0.05));
            var online = n - offline;
            var critical = (int)Math.Round(online * (0.01 + random.NextDouble() * 0.05));
            var warning = (int)Math.Round(online * (0.05 + random.NextDouble() * 0.1));
            return new LocationPlan(n, online - critical - warning, warning, critical, offline, Math.Min(unlicensedPerLocation[i], n));
        }).ToArray();
    }

    /// <summary>Adds the devices of one customer. Licensed devices take the fingerprints the fake licence knows (1..licensed).</summary>
    public void AddCustomer(Tenant tenant, string code, IReadOnlyList<Location> locations, IReadOnlyList<LocationPlan> plans, FakeLicense? license, IReadOnlyList<string> features, string planCode, int tenantNumber)
    {
        var licensedIndex = 0;
        var unlicensedIndex = plans.Sum(p => p.Devices - p.Unlicensed);
        for (var l = 0; l < plans.Count; l++)
        {
            var plan = plans[l];
            var location = locations[l];
            var fixedDevices = code == "ACME" && l == 0 ? CairoFixed : [];
            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in fixedDevices)
            {
                var dash = f.Name.LastIndexOf('-');
                var prefix = f.Name[..dash];
                counters[prefix] = Math.Max(counters.GetValueOrDefault(prefix), int.Parse(f.Name[(dash + 1)..], System.Globalization.CultureInfo.InvariantCulture));
            }

            // The statuses still to hand out after the fixed devices.
            var healthy = plan.Healthy - fixedDevices.Count(f => f.Online && f.Health == DeviceHealth.Healthy);
            var warning = plan.Warning - fixedDevices.Count(f => f.Online && f.Health == DeviceHealth.Warning);
            var critical = plan.Critical - fixedDevices.Count(f => f.Online && f.Health == DeviceHealth.Critical);
            var offline = plan.Offline - fixedDevices.Count(f => !f.Online);
            var unlicensed = plan.Unlicensed - fixedDevices.Count(f => !f.Licensed);
            var statuses = Shuffle(Enumerable.Repeat(DeviceHealth.Critical, critical)
                .Concat(Enumerable.Repeat(DeviceHealth.Warning, warning))
                .Concat(Enumerable.Repeat(DeviceHealth.Healthy, healthy))
                .Concat(Enumerable.Repeat(DeviceHealth.Unknown, offline)).ToList());

            var osCounts = plan.OsCounts is { } fixedCounts
                ? fixedCounts.Select((c, i) => c - fixedDevices.Count(f => f.Os == Families[i])).ToArray()
                : LargestRemainder(statuses.Count, [.. OsRatio.Select(r => (double)r)]);
            var oses = Shuffle(osCounts.SelectMany((c, i) => Enumerable.Repeat(Families[i], c)).ToList());

            // Unlicensed devices are picked among the generated online, non-critical ones (inside the grace period).
            // The count must be exact (the fake licence holds the others), so other slots are used when needed.
            var preferred = Enumerable.Range(0, statuses.Count).Where(i => statuses[i] is DeviceHealth.Healthy or DeviceHealth.Warning).ToList();
            var others = Enumerable.Range(0, statuses.Count).Where(i => statuses[i] is not (DeviceHealth.Healthy or DeviceHealth.Warning)).ToList();
            var unlicensedSlots = Shuffle(preferred).Concat(Shuffle(others)).Take(unlicensed).ToHashSet();

            var hostNumber = 0;
            foreach (var f in fixedDevices)
            {
                var fp = f.Licensed ? DemoLicensing.Fingerprint(code, ++licensedIndex) : DemoLicensing.Fingerprint(code, ++unlicensedIndex);
                var device = AddDevice(tenant, location, fp, f.Name, f.Os, f.OsName, f.OsVersion, f.Ip, f.Online, f.Licensed, f.Critical, f.Warning,
                    f.Online ? null : now.AddHours(-f.OfflineHours), f.Health == DeviceHealth.Critical, license, features, planCode);
                AddInventory(device, f, full: f.Name == "WEB-SRV-01");
                hostNumber++;
            }

            for (var i = 0; i < statuses.Count; i++)
            {
                var os = oses[i];
                var prefix = Prefixes[os][random.Next(Prefixes[os].Length)];
                var number = counters[prefix] = counters.GetValueOrDefault(prefix) + 1;
                var (osName, osVersion) = OsNames[os][random.Next(OsNames[os].Length)];
                var isUnlicensed = unlicensedSlots.Contains(i);
                var fp = isUnlicensed ? DemoLicensing.Fingerprint(code, ++unlicensedIndex) : DemoLicensing.Fingerprint(code, ++licensedIndex);
                var health = statuses[i];
                var (crit, warn) = health switch
                {
                    DeviceHealth.Critical => (1, random.Next(0, 3)),
                    DeviceHealth.Warning => (0, random.Next(1, 3)),
                    DeviceHealth.Unknown => (random.Next(0, 2), 0),
                    _ => (0, 0),
                };
                hostNumber++;
                var ip = code == "ACME" && l == 0
                    ? $"192.168.{2 + (hostNumber / 200)}.{10 + (hostNumber % 200)}"
                    : $"10.{tenantNumber % 250}.{(l * 2) + (hostNumber / 240)}.{10 + (hostNumber % 240)}";
                AddDevice(tenant, location, fp, $"{prefix}-{number:00}", os, osName, osVersion, ip, health != DeviceHealth.Unknown, !isUnlicensed, crit, warn,
                    health == DeviceHealth.Unknown ? now.AddMinutes(-random.Next(10, 3 * 24 * 60)) : null, health == DeviceHealth.Critical, license, features, planCode);
            }
        }
    }

    private Device AddDevice(
        Tenant tenant, Location location, string fingerprint, string name, OsFamily os, string osName, string osVersion, string ip, bool online, bool licensed,
        int critical, int warning, DateTimeOffset? offlineSince, bool busy, FakeLicense? license, IReadOnlyList<string> features, string planCode)
    {
        var enrolledAt = now.AddDays(-random.Next(0, Math.Max(1, Math.Min(900, (int)(now - new DateTimeOffset(tenant.CustomerSince.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)).TotalDays))))
            .AddMinutes(-random.Next(0, 24 * 60));
        var architecture = os == OsFamily.MacOS ? "arm64" : "x64";
        var info = new AgentInfo(name, os, osName, osVersion, architecture, "1.1.0", 1, ip, $"203.0.113.{1 + random.Next(254)}", Mac());
        var device = Device.Enroll(tenant.Id, location.Id, fingerprint, info, enrolledAt);
        device.ClearDomainEvents();

        var state = DeviceState.Create(device.Id, tenant.Id, location.Id, os, LicenseStateValue.Licensed, enrolledAt);
        if (!licensed)
            state.SetLicense(LicenseStateValue.Unlicensed, now.AddDays(-random.Next(1, 8)), grace);
        var lastSeen = offlineSince ?? now.AddSeconds(-random.Next(5, 60));
        var connectedSince = online ? now.AddMinutes(-random.Next(30, 30 * 24 * 60)) : (DateTimeOffset?)null;
        state.SeedAs(online ? ConnectionState.Online : ConnectionState.Offline, lastSeen, connectedSince, now, grace);
        state.SetOpenAlerts(critical, warning, now, grace);
        var cpu = busy ? 85 + random.Next(0, 14) : warning > 0 ? 45 + random.Next(0, 35) : 5 + random.Next(0, 40);
        var ram = busy ? 70 + random.Next(0, 25) : 30 + random.Next(0, 45);
        var disk = 20 + random.Next(0, 65);
        state.SetMetrics(cpu + (random.Next(0, 10) / 10m), ram + (random.Next(0, 10) / 10m), disk + (random.Next(0, 10) / 10m),
            connectedSince is { } since ? (long)(now - since).TotalSeconds : null, lastSeen);
        db.AddRange(device, state);

        if (license is not null)
        {
            var checkAfter = now.AddMinutes(random.Next(5, 24 * 60));
            var offlineUntil = checkAfter.Add(FakeLicensingGateway.OfflineGrace);
            var token = signer?.Sign(license, fingerprint, features, planCode, now, checkAfter, offlineUntil) ?? string.Empty;
            var seat = DeviceLicense.Licensed(device.Id, tenant.Id, fingerprint, license.Id, license.LicenseNumber, token, signer?.Kid ?? string.Empty, checkAfter, offlineUntil, enrolledAt);
            if (!licensed)
                seat.Unlicense("SEAT_RELEASED", state.UnlicensedSince ?? now);
            seat.ClearDomainEvents();
            db.Add(seat);
        }

        Created++;
        return device;
    }

    private void AddInventory(Device device, Fixed f, bool full)
    {
        void Add(InventoryKind kind, object document)
        {
            var (bytes, hash) = InventoryCodec.Encode(JsonSerializer.Serialize(document, FakeLicensingStore.Json));
            db.Add(InventoryDocument.Create(device.Id, device.TenantId, kind, bytes, hash, now));
        }

        var server = f.OsName.Contains("Server", StringComparison.Ordinal);
        Add(InventoryKind.Hardware, new
        {
            manufacturer = f.Os == OsFamily.MacOS ? "Apple" : "Dell Inc.",
            model = f.Os == OsFamily.MacOS ? "MacBook Pro (14-inch, 2023)" : server ? "PowerEdge R650" : "OptiPlex 7010",
            serialNumber = $"SN-{device.Fingerprint.ToUpperInvariant()}",
            cpu = new { model = f.Os == OsFamily.MacOS ? "Apple M2 Pro" : "Intel Xeon Silver 4314", cores = server ? 16 : 8, logicalProcessors = server ? 32 : 8, speedGhz = 2.4 },
            memory = new { totalGb = server ? 64 : 16, slots = server ? 8 : 2 },
            bios = new { vendor = f.Os == OsFamily.MacOS ? "Apple" : "Dell Inc.", version = "1.12.1" },
        });
        Add(InventoryKind.Os, new { name = f.OsName, version = f.OsVersion, architecture = f.Os == OsFamily.MacOS ? "arm64" : "x64", hostname = f.Name, timeZone = "Africa/Cairo", installedAt = now.AddYears(-2) });
        if (!full)
            return;

        Add(InventoryKind.Network, new[]
        {
            new { adapter = "Ethernet0", mac = "00-15-5D-00-01-10", ipv4 = f.Ip, gateway = "192.168.1.1", dns = DnsServers, speedMbps = 1000 },
        });
        Add(InventoryKind.Disks, new[]
        {
            new { drive = "C:", label = "System", fileSystem = "NTFS", totalGb = 250, usedPercent = 92 },
            new { drive = "D:", label = "Data", fileSystem = "NTFS", totalGb = 1000, usedPercent = 76 },
            new { drive = "E:", label = "Backup", fileSystem = "NTFS", totalGb = 2000, usedPercent = 45 },
        });
        string[] vendors = ["Microsoft Corporation", "Mozilla", "Oracle", "Adobe", "7-Zip", "Notepad++ Team", "Python Software Foundation", "Git"];
        Add(InventoryKind.Programs, Enumerable.Range(1, 40).Select(i => new { name = $"Sample Program {i:00}", version = $"{1 + (i % 5)}.{i % 10}.0", publisher = vendors[i % vendors.Length], installedAt = now.AddDays(-i * 9).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }).ToArray());
        Add(InventoryKind.Services, Enumerable.Range(1, 60).Select(i => new { name = $"SampleService{i:00}", displayName = $"Sample Service {i:00}", status = i % 7 == 0 ? "Stopped" : "Running", startType = i % 7 == 0 ? "Manual" : "Automatic" }).ToArray());
        Add(InventoryKind.Users, LocalUsers.Select((u, i) => new { name = u, enabled = u != "guest", lastLogon = now.AddDays(-i) }).ToArray());
    }

    private string Mac()
    {
        var bytes = new byte[6];
        random.NextBytes(bytes);
        bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02);
        return string.Join('-', bytes.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private List<T> Shuffle<T>(List<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    /// <summary>Splits <paramref name="total"/> by weight so the parts add up exactly.</summary>
    public static int[] LargestRemainder(int total, double[] weights)
    {
        var sum = weights.Sum();
        if (total <= 0 || sum <= 0)
            return new int[weights.Length];
        var exact = weights.Select(w => total * w / sum).ToArray();
        var parts = exact.Select(e => (int)Math.Floor(e)).ToArray();
        foreach (var i in Enumerable.Range(0, weights.Length).OrderByDescending(i => exact[i] - parts[i]).ThenBy(i => i).Take(total - parts.Sum()))
            parts[i]++;
        return parts;
    }
}
