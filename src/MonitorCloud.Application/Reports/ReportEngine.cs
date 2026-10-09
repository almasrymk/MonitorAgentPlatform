using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Application.Telemetry.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;

namespace MonitorCloud.Application.Reports;

/// <summary>
/// Builds the eight reports (MC-901) from the other modules' read contracts, in the caller's scope. With
/// <c>platform</c> = true (cross-customer, unrestricted scope) the rows carry the customer.
/// </summary>
public sealed class ReportEngine(
    IDeviceReportReader devices, ITelemetryReportReader telemetry, IAlertReportReader alerts, ILocationLookup locations, ITenantNames tenants, IDeviceLicenseStats licenses,
    IEntitlementDirectory entitlements)
{
    public async Task<ReportData> BuildAsync(string type, ReportParameters p, Guid? tenantId, bool platform, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        var definition = ReportCatalog.Find(type) ?? throw new ArgumentException($"Unknown report type '{type}'.", nameof(type));
        var deviceRows = await devices.ListAsync(p.LocationIds, p.DeviceIds, ct);
        var locationNames = await locations.GetAsync([.. deviceRows.Select(d => d.LocationId).Concat(p.LocationIds).Distinct()], ct);
        var customerNames = platform ? await tenants.GetAsync([.. deviceRows.Select(d => d.TenantId).Distinct()], ct) : new Dictionary<Guid, string>();
        string Location(Guid id) => locationNames.GetValueOrDefault(id)?.Name ?? string.Empty;
        string Customer(Guid id) => customerNames.GetValueOrDefault(id) ?? string.Empty;
        var byLocation = p.GroupBy == "location";

        switch (definition.Type)
        {
            case "overview":
            {
                var periodAlerts = await alerts.ListAsync(p.From, p.To, p.LocationIds, p.DeviceIds, ct);
                var summary = DeviceSummary(deviceRows).Concat(SeveritySummary(periodAlerts)).ToList();
                var groups = platform ? deviceRows.GroupBy(d => (d.TenantId, Guid.Empty)) : deviceRows.GroupBy(d => (d.TenantId, d.LocationId));
                var rows = groups
                    .Select(g => Row(platform ? Customer(g.Key.TenantId) : Location(g.Key.Item2), g.Count(), g.Count(d => d.Connection == "Online"), g.Count(d => d.Health == "Healthy"),
                        g.Count(d => d.Health == "Warning"), g.Count(d => d.Health == "Critical"), periodAlerts.Count(a => platform ? a.TenantId == g.Key.TenantId : a.LocationId == g.Key.Item2)))
                    .OrderBy(r => r[0] as string, StringComparer.CurrentCulture).ToList();
                return new ReportData(definition.Title, p.From, p.To, summary,
                    [new(platform ? "customer" : "location", platform ? "Customer" : "Location"), N("devices", "Devices"), N("online", "Online"), N("healthy", "Healthy"),
                     N("warning", "Warning"), N("critical", "Critical"), N("alerts", "Alerts")], rows);
            }

            case "location-summary":
            {
                var rows = deviceRows.GroupBy(d => d.LocationId)
                    .Select(g =>
                    {
                        var info = locationNames.GetValueOrDefault(g.Key);
                        var total = g.Count();
                        var healthy = g.Count(d => d.Health == "Healthy");
                        return Prefix(platform, Customer(g.First().TenantId), info?.Name ?? string.Empty, info?.City, total, g.Count(d => d.Connection == "Online"),
                            g.Count(d => d.Connection != "Online"), healthy, g.Count(d => d.Health == "Warning"), g.Count(d => d.Health == "Critical"),
                            g.Count(d => d.LicenseState == "Licensed"), total == 0 ? 0m : Math.Round(100m * healthy / total, 1));
                    })
                    .OrderBy(r => string.Join('|', r.Take(platform ? 2 : 1)), StringComparer.CurrentCulture).ToList();
                return new ReportData(definition.Title, p.From, p.To, DeviceSummary(deviceRows),
                    Columns(platform, new("location", "Location"), new("city", "City"), N("devices", "Devices"), N("online", "Online"), N("offline", "Offline"), N("healthy", "Healthy"),
                        N("warning", "Warning"), N("critical", "Critical"), N("licensed", "Licensed"), N("healthScore", "Health %")), rows);
            }

            case "device-health":
            {
                var rows = deviceRows.Select(d => Prefix(platform, Customer(d.TenantId), d.Name, Location(d.LocationId), d.OsName ?? d.OsFamily, d.Connection, d.Health, d.LicenseState,
                    d.Cpu, d.Ram, d.Disk, d.OpenCritical + d.OpenWarning, d.LastSeenAt)).ToList();
                return new ReportData(definition.Title, p.From, p.To, DeviceSummary(deviceRows),
                    Columns(platform, new("device", "Device"), new("location", "Location"), new("os", "Operating system"), new("connection", "Connection"), new("health", "Health"),
                        new("license", "Licence"), N("cpu", "CPU %"), N("ram", "RAM %"), N("disk", "Disk %"), N("openAlerts", "Open alerts"), new("lastSeen", "Last seen (UTC)")), rows);
            }

            case "performance":
            {
                var usage = await telemetry.UsageAsync([.. deviceRows.Select(d => d.Id)], p.From, p.To, ct);
                IReadOnlyList<IReadOnlyList<object?>> rows = byLocation
                    ? [.. deviceRows.GroupBy(d => d.LocationId).Select(g =>
                        {
                            var u = g.Select(d => usage.GetValueOrDefault(d.Id)).OfType<DeviceUsage>().ToList();
                            return Prefix(platform, Customer(g.First().TenantId), Location(g.Key), g.Count(), Avg(u.Select(x => x.CpuAvg)), Max(u.Select(x => x.CpuMax)),
                                Avg(u.Select(x => x.RamAvg)), Max(u.Select(x => x.RamMax)), Max(u.Select(x => x.DiskMax)));
                        })]
                    : [.. deviceRows.Select(d =>
                        {
                            var u = usage.GetValueOrDefault(d.Id);
                            return Prefix(platform, Customer(d.TenantId), d.Name, Location(d.LocationId), u?.CpuAvg, u?.CpuMax, u?.RamAvg, u?.RamMax, u?.DiskMax, u?.Hours ?? 0);
                        })];
                var all = usage.Values.ToList();
                return new ReportData(definition.Title, p.From, p.To,
                    [new("Devices", deviceRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("Average CPU", ReportRenderer.Format(Avg(all.Select(x => x.CpuAvg))) + " %"),
                     new("Average RAM", ReportRenderer.Format(Avg(all.Select(x => x.RamAvg))) + " %"), new("Highest disk", ReportRenderer.Format(Max(all.Select(x => x.DiskMax))) + " %")],
                    byLocation
                        ? Columns(platform, new("location", "Location"), N("devices", "Devices"), N("cpuAvg", "CPU avg %"), N("cpuMax", "CPU max %"), N("ramAvg", "RAM avg %"),
                            N("ramMax", "RAM max %"), N("diskMax", "Disk max %"))
                        : Columns(platform, new("device", "Device"), new("location", "Location"), N("cpuAvg", "CPU avg %"), N("cpuMax", "CPU max %"), N("ramAvg", "RAM avg %"),
                            N("ramMax", "RAM max %"), N("diskMax", "Disk max %"), N("hours", "Hours")),
                    rows);
            }

            case "network-usage":
            {
                var usage = await telemetry.UsageAsync([.. deviceRows.Select(d => d.Id)], p.From, p.To, ct);
                static decimal Gb(long bytes) => Math.Round(bytes / 1_000_000_000m, 2);
                var rows = deviceRows.Select(d =>
                    {
                        var u = usage.GetValueOrDefault(d.Id);
                        return Prefix(platform, Customer(d.TenantId), d.Name, Location(d.LocationId), Gb(u?.RxBytes ?? 0), Gb(u?.TxBytes ?? 0), Gb((u?.RxBytes ?? 0) + (u?.TxBytes ?? 0)));
                    })
                    .ToList();
                var rx = usage.Values.Sum(u => u.RxBytes);
                var tx = usage.Values.Sum(u => u.TxBytes);
                return new ReportData(definition.Title, p.From, p.To,
                    [new("Download", $"{ReportRenderer.Format(Gb(rx))} GB"), new("Upload", $"{ReportRenderer.Format(Gb(tx))} GB"), new("Total", $"{ReportRenderer.Format(Gb(rx + tx))} GB")],
                    Columns(platform, new("device", "Device"), new("location", "Location"), N("downloadGb", "Download GB"), N("uploadGb", "Upload GB"), N("totalGb", "Total GB")), rows);
            }

            case "alerts":
            {
                var periodAlerts = await alerts.ListAsync(p.From, p.To, p.LocationIds, p.DeviceIds, ct);
                var names = deviceRows.ToDictionary(d => d.Id, d => d.Name);
                if (platform)
                    customerNames = await tenants.GetAsync([.. periodAlerts.Select(a => a.TenantId).Distinct()], ct);
                var extraLocations = await locations.GetAsync([.. periodAlerts.Select(a => a.LocationId).Distinct()], ct);
                var rows = periodAlerts.Select(a => Prefix(platform, Customer(a.TenantId), a.FirstSeenAt, a.Severity, a.Category, names.GetValueOrDefault(a.DeviceId) ?? string.Empty,
                    extraLocations.GetValueOrDefault(a.LocationId)?.Name ?? string.Empty, a.Title, a.Status, a.ResolvedAt, a.Occurrences)).ToList();
                return new ReportData(definition.Title, p.From, p.To, SeveritySummary(periodAlerts).Append(new("Resolved", periodAlerts.Count(a => a.Status == "Resolved").ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList(),
                    Columns(platform, new("opened", "Opened (UTC)"), new("severity", "Severity"), new("category", "Category"), new("device", "Device"), new("location", "Location"),
                        new("title", "Title"), new("status", "Status"), new("resolved", "Resolved (UTC)"), N("occurrences", "Occurrences")), rows);
            }

            case "license-usage":
            {
                if (platform)
                {
                    var tenantIds = deviceRows.Select(d => d.TenantId).Distinct().ToList();
                    var plans = await entitlements.GetAsync(tenantIds, ct);
                    var rows = deviceRows.GroupBy(d => d.TenantId).Select(g =>
                        {
                            var e = plans.GetValueOrDefault(g.Key);
                            return (IReadOnlyList<object?>)[Customer(g.Key), e?.PlanName, g.Count(d => d.LicenseState == "Licensed"), g.Count(d => d.LicenseState != "Licensed"), e?.MaxDevices];
                        })
                        .OrderBy(r => r[0] as string, StringComparer.CurrentCulture).ToList();
                    return new ReportData(definition.Title, p.From, p.To, LicenseSummary(deviceRows),
                        [new("customer", "Customer"), new("plan", "Plan"), N("licensed", "Licensed"), N("unlicensed", "Unlicensed"), N("limit", "Seat limit")], rows);
                }

                var (licensed, unlicensed, byOs) = await licenses.GetAsync(ct);
                var entitlement = tenantId is { } t ? (await entitlements.GetAsync([t], ct)).GetValueOrDefault(t) : null;
                var summary = new List<ReportSummaryItem>
                {
                    new("Plan", entitlement?.PlanName ?? "-"),
                    new("Licensed", licensed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new("Unlicensed", unlicensed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new("Seat limit", entitlement?.MaxDevices?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"),
                };
                return new ReportData(definition.Title, p.From, p.To, summary, [new("os", "Operating system"), N("devices", "Licensed devices"), N("percent", "% of licensed")],
                    [.. byOs.Select(o => (IReadOnlyList<object?>)[o.OsFamily, o.Devices, o.Percent])]);
            }

            default:
            {
                var usage = await telemetry.UsageAsync([.. deviceRows.Select(d => d.Id)], p.From, p.To, ct);
                var periodAlerts = await alerts.ListAsync(p.From, p.To, p.LocationIds, p.DeviceIds, ct);
                var counts = periodAlerts.GroupBy(a => a.DeviceId).ToDictionary(g => g.Key, g => g.Count());
                var rows = deviceRows.Select(d =>
                    {
                        var u = usage.GetValueOrDefault(d.Id);
                        return Prefix(platform, Customer(d.TenantId), d.Name, Location(d.LocationId), d.Health, d.LicenseState, u?.CpuAvg, u?.RamAvg, u?.DiskMax, counts.GetValueOrDefault(d.Id));
                    })
                    .ToList();
                return new ReportData(definition.Title, p.From, p.To, DeviceSummary(deviceRows),
                    Columns(platform, new("device", "Device"), new("location", "Location"), new("health", "Health"), new("license", "Licence"), N("cpuAvg", "CPU avg %"), N("ramAvg", "RAM avg %"),
                        N("diskMax", "Disk max %"), N("alerts", "Alerts")), rows);
            }
        }
    }

    private static ReportColumn N(string key, string label) => new(key, label, true);

    private static IReadOnlyList<object?> Row(params object?[] values) => values;

    private static IReadOnlyList<object?> Prefix(bool platform, string customer, params object?[] values) => platform ? [customer, .. values] : values;

    private static IReadOnlyList<ReportColumn> Columns(bool platform, params ReportColumn[] columns) => platform ? [new("customer", "Customer"), .. columns] : columns;

    private static decimal? Avg(IEnumerable<decimal?> values)
    {
        var list = values.OfType<decimal>().ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2);
    }

    private static decimal? Max(IEnumerable<decimal?> values)
    {
        var list = values.OfType<decimal>().ToList();
        return list.Count == 0 ? null : list.Max();
    }

    private static IReadOnlyList<ReportSummaryItem> DeviceSummary(IReadOnlyList<DeviceReportRow> rows) =>
    [
        new("Devices", Count(rows.Count)),
        new("Online", Count(rows.Count(d => d.Connection == "Online"))),
        new("Offline", Count(rows.Count(d => d.Connection != "Online"))),
        new("Healthy", Count(rows.Count(d => d.Health == "Healthy"))),
        new("Warning", Count(rows.Count(d => d.Health == "Warning"))),
        new("Critical", Count(rows.Count(d => d.Health == "Critical"))),
        new("Licensed", Count(rows.Count(d => d.LicenseState == "Licensed"))),
        new("Unlicensed", Count(rows.Count(d => d.LicenseState != "Licensed"))),
    ];

    private static IReadOnlyList<ReportSummaryItem> LicenseSummary(IReadOnlyList<DeviceReportRow> rows) =>
        [new("Licensed", Count(rows.Count(d => d.LicenseState == "Licensed"))), new("Unlicensed", Count(rows.Count(d => d.LicenseState != "Licensed")))];

    private static IReadOnlyList<ReportSummaryItem> SeveritySummary(IReadOnlyList<AlertReportRow> alerts) =>
    [
        new("Alerts", Count(alerts.Count)),
        new("Critical alerts", Count(alerts.Count(a => a.Severity == "Critical"))),
        new("Warning alerts", Count(alerts.Count(a => a.Severity == "Warning"))),
        new("Info alerts", Count(alerts.Count(a => a.Severity == "Info"))),
    ];

    private static string Count(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
