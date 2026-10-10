using Microsoft.EntityFrameworkCore;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Seeding;

/// <summary>
/// Seed part 4 (M6, 08 section 4): open alerts that match the seeded health counts, resolved alerts over 30 days with
/// a peak three days ago, <c>AlertDailyStats</c> from both, 25 notifications per feed (3 unread), 60 platform audit
/// records, monitor points of WEB-SRV-01 and alert recipients of the detailed customers.
/// </summary>
internal sealed class DemoMonitoring(AppDbContext db, DateTimeOffset now, Random random)
{
    private sealed record Issue(string Key, string Category, string Title, string Message);

    private static readonly Issue[] Critical =
    [
        new("cpu", AlertCategories.Performance, "High CPU usage", "CPU above 90% for 15 minutes."),
        new("disk-C", AlertCategories.Storage, "Disk space critically low", "Drive C: has less than 5% free space."),
        new("ram", AlertCategories.Performance, "Memory exhausted", "Memory usage above 95%."),
        new("point:db", AlertCategories.Database, "Database not responding", "The database check timed out."),
    ];

    private static readonly Issue[] Warning =
    [
        new("disk-D", AlertCategories.Storage, "Disk space low", "Drive D: has less than 15% free space."),
        new("ram", AlertCategories.Performance, "High memory usage", "Memory usage above 85%."),
        new("point:svc", AlertCategories.Service, "Service response slow", "The service answered in more than 2 seconds."),
        new("point:app", AlertCategories.Application, "Application restarted", "The application restarted twice in an hour."),
        new("sensor:cpu", AlertCategories.System, "CPU temperature high", "CPU temperature above 80 °C."),
    ];

    private static readonly Issue[] Info =
    [
        new("hw:update", AlertCategories.System, "Updates available", "Operating system updates are waiting."),
        new("point:backup", AlertCategories.Service, "Backup finished late", "The nightly backup took longer than usual."),
    ];

    /// <summary>The fixed devices of Acme / Cairo HQ (08 section 3) and their open alerts.</summary>
    private static readonly Dictionary<string, (AlertSeverity Severity, Issue Issue)[]> Fixed = new(StringComparer.Ordinal)
    {
        ["WEB-SRV-01"] =
        [
            (AlertSeverity.Critical, new("cpu", AlertCategories.Performance, "High CPU usage", "CPU at 92% for the last 20 minutes.")),
            (AlertSeverity.Warning, new("disk-D", AlertCategories.Storage, "Disk D: space low", "Drive D: is 76% full.")),
            (AlertSeverity.Warning, new("point:iis", AlertCategories.Service, "IIS response slow", "IIS answered in 2.4 seconds.")),
        ],
        ["DB-SRV-01"] = [(AlertSeverity.Warning, new("disk-/", AlertCategories.Storage, "Disk space low", "The root file system is 88% full."))],
        ["APP-SRV-02"] =
        [
            (AlertSeverity.Critical, new(CloudIssues.DeviceOffline, AlertCategories.Connectivity, "Device is offline", "The device stopped sending heartbeats.")),
            (AlertSeverity.Warning, new("point:app", AlertCategories.Application, "Application not running", "The order service is not running.")),
        ],
        ["BR-DC-01"] = [(AlertSeverity.Warning, new("point:dns", AlertCategories.Service, "Service response slow", "DNS answered in 1.8 seconds."))],
        ["SQL-DB-01"] = [(AlertSeverity.Critical, new("disk-/", AlertCategories.Storage, "Disk space critically low", "The root file system is 97% full."))],
        ["DEV-MAC-01"] = [(AlertSeverity.Warning, new("ram", AlertCategories.Performance, "High memory usage", "Memory usage at 88%."))],
    };

    public int Alerts { get; private set; }

    public async Task RunAsync(IReadOnlyCollection<string> detailedCodes, CancellationToken ct)
    {
        var tenants = await db.Set<Tenant>().AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var detailed = tenants.Values.Where(t => detailedCodes.Contains(t.Code)).Select(t => t.Id).ToHashSet();
        var states = await db.Set<DeviceState>().AsNoTracking()
            .Where(s => s.OpenCritical > 0 || s.OpenWarning > 0)
            .Select(s => new { s.DeviceId, s.TenantId, s.LocationId, s.OpenCritical, s.OpenWarning, s.Connection, s.LastSeenAt })
            .ToListAsync(ct);
        var names = await db.Set<Device>().AsNoTracking().Select(d => new { d.Id, d.Name, d.TenantId, d.LocationId }).ToListAsync(ct);
        var nameById = names.ToDictionary(n => n.Id, n => n.Name);
        var acme = tenants.Values.FirstOrDefault(t => t.Code == "ACME")?.Id;
        var cairo = await db.Set<Location>().AsNoTracking().Where(l => l.TenantId == acme && l.Code == "CAIRO-HQ").Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        var alerts = new List<Alert>();

        // Open alerts: exactly OpenCritical critical and OpenWarning warning alerts per device.
        foreach (var s in states)
        {
            var name = nameById.GetValueOrDefault(s.DeviceId) ?? string.Empty;
            var fixedIssues = s.LocationId == cairo && Fixed.TryGetValue(name, out var f) ? f : null;
            var offline = s.Connection == ConnectionState.Offline;
            var since = offline && s.LastSeenAt is { } seen ? new DateTimeOffset(seen, TimeSpan.Zero).AddMinutes(2) : now.AddMinutes(-random.Next(20, 48 * 60));
            var wanted = fixedIssues?.ToList() ?? Pick(s.OpenCritical, s.OpenWarning, offline);
            foreach (var (severity, issue) in wanted)
            {
                var source = issue.Key == CloudIssues.DeviceOffline ? AlertSource.Cloud : AlertSource.Agent;
                var first = issue.Key == CloudIssues.DeviceOffline ? since : since.AddMinutes(random.Next(0, 30));
                var alert = Alert.Raise(s.TenantId, s.LocationId, s.DeviceId, issue.Key, issue.Category, severity, issue.Title, issue.Message, source, Min(first, now), false);
                alert.ClearDomainEvents();
                // Repeated reports: the last one a few minutes ago at most.
                var lastSeen = Min(first.AddMinutes(random.Next(5, 600)), now.AddMinutes(-random.Next(1, 20)));
                for (var i = random.Next(0, 12); i > 0; i--)
                    alert.Touch(severity, null, null, lastSeen);
                alert.ClearDomainEvents();
                alerts.Add(alert);
            }
        }

        // Resolved alerts of the detailed customers: a curve over 30 days with its peak three days ago.
        foreach (var tenantId in detailed)
        {
            var devices = names.Where(n => n.TenantId == tenantId).ToList();
            if (devices.Count == 0)
                continue;
            var scale = Math.Max(1, devices.Count / 40.0);
            for (var day = 0; day < 30; day++)
            {
                // The peak is high enough to stay the maximum after the open alerts of the last two days are added.
                var count = (int)Math.Round(scale * (1.5 + 14 * Math.Exp(-Math.Pow(day - 3, 2) / 4.0)) + random.Next(0, 2));
                for (var i = 0; i < count; i++)
                {
                    var device = devices[random.Next(devices.Count)];
                    var roll = random.Next(100);
                    var (severity, pool) = roll < 25 ? (AlertSeverity.Critical, Critical) : roll < 75 ? (AlertSeverity.Warning, Warning) : (AlertSeverity.Info, Info);
                    var issue = pool[random.Next(pool.Length)];
                    var first = now.AddDays(-day).AddMinutes(-random.Next(60, 23 * 60));
                    var alert = Alert.Raise(tenantId, device.LocationId, device.Id, issue.Key, issue.Category, severity, issue.Title, issue.Message, AlertSource.Agent, first, false);
                    alert.SeedResolved(Min(first.AddMinutes(random.Next(10, 240)), now));
                    alert.ClearDomainEvents();
                    alerts.Add(alert);
                }
            }
        }

        db.Set<Alert>().AddRange(alerts);
        Alerts = alerts.Count;

        // AlertDailyStats: alerts opened per tenant-local day.
        var stats = alerts
            .GroupBy(a => (a.TenantId, a.LocationId, Day: LocalDay(a.FirstSeenAt, tenants[a.TenantId].TimeZone)))
            .Select(g =>
            {
                var stat = AlertDailyStat.Create(g.Key.TenantId, g.Key.LocationId, g.Key.Day);
                stat.Count(AlertSeverity.Critical, g.Count(a => a.Severity == AlertSeverity.Critical));
                stat.Count(AlertSeverity.Warning, g.Count(a => a.Severity == AlertSeverity.Warning));
                stat.Count(AlertSeverity.Info, g.Count(a => a.Severity == AlertSeverity.Info));
                return stat;
            });
        db.Set<AlertDailyStat>().AddRange(stats);

        await AddNotificationsAsync(detailed, alerts, nameById, ct);
        AddAudit(tenants.Values.ToList());
        await AddMonitorPointsAsync(ct);
        await AddRecipientsAsync(tenants.Values.Where(t => detailed.Contains(t.Id)).ToList(), ct);
        await db.SaveChangesAsync(ct);
    }

    private List<(AlertSeverity, Issue)> Pick(int critical, int warning, bool offline)
    {
        var list = new List<(AlertSeverity, Issue)>();
        var criticalPool = Critical.OrderBy(_ => random.Next()).ToList();
        for (var i = 0; i < critical; i++)
        {
            list.Add(offline && i == 0
                ? (AlertSeverity.Critical, new Issue(CloudIssues.DeviceOffline, AlertCategories.Connectivity, "Device is offline", "The device stopped sending heartbeats."))
                : (AlertSeverity.Critical, criticalPool[i % criticalPool.Count]));
        }

        var warningPool = Warning.Where(w => list.All(c => c.Item2.Key != w.Key)).OrderBy(_ => random.Next()).ToList();
        for (var i = 0; i < warning && i < warningPool.Count; i++)
            list.Add((AlertSeverity.Warning, warningPool[i]));
        return list;
    }

    private async Task AddNotificationsAsync(HashSet<Guid> detailed, List<Alert> alerts, Dictionary<Guid, string> names, CancellationToken ct)
    {
        var users = await db.Set<User>().AsNoTracking().Select(u => new { u.Id, u.TenantId }).ToListAsync(ct);
        var locations = await db.Set<Location>().AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        void Feed(Guid? tenantId, IReadOnlyList<Alert> source)
        {
            var readers = users.Where(u => u.TenantId == tenantId).Select(u => u.Id).ToList();
            var picked = source.OrderByDescending(a => a.FirstSeenAt).Take(25).ToList();
            for (var i = 0; i < picked.Count; i++)
            {
                var a = picked[i];
                var device = names.GetValueOrDefault(a.DeviceId) ?? "A device";
                var location = locations.GetValueOrDefault(a.LocationId);
                var title = tenantId is null ? $"{a.Title} ({device})" : a.Title;
                var notification = Notification.Create(tenantId, a.Severity.ToString(), a.Category, title, location is null ? device : $"{device} - {location}", a.LocationId,
                    a.DeviceId, a.Id, a.FirstSeenAt);
                db.Set<Notification>().Add(notification);

                // The three newest stay unread.
                if (i >= 3)
                {
                    foreach (var reader in readers)
                        db.Set<NotificationRead>().Add(NotificationRead.Create(notification.Id, reader, a.FirstSeenAt.AddMinutes(30)));
                }
            }
        }

        foreach (var tenantId in detailed)
            Feed(tenantId, alerts.Where(a => a.TenantId == tenantId && a.Severity != AlertSeverity.Info).ToList());
        Feed(null, alerts.Where(a => a.Severity == AlertSeverity.Critical).ToList());
    }

    private void AddAudit(IReadOnlyList<Tenant> tenants)
    {
        var actions = new (string Action, string Entity, string Details)[]
        {
            ("auth.login", "User", "Signed in"),
            ("device.enrolled", "Device", "New device registered"),
            ("tenant.plan_updated", "Tenant", "Subscription plan updated"),
            ("device.retired", "Device", "Device archived"),
            ("user.invited", "User", "User added"),
        };
        for (var i = 0; i < 60; i++)
        {
            var (action, entity, details) = actions[i % actions.Length];
            var tenant = tenants[random.Next(tenants.Count)];
            var actor = action == "auth.login" ? "Admin User" : i % 3 == 0 ? "Support User" : "Admin User";
            var at = now.AddMinutes(-(int)(i * (7 * 24 * 60 / 60.0)) - random.Next(0, 90));
            db.Set<AuditRecord>().Add(AuditRecord.Create(
                action == "auth.login" ? null : tenant.Id, AuditActorType.User, null, actor, action, entity, Guid.CreateVersion7().ToString(), true,
                action == "auth.login" ? details : $"{details} - {tenant.Name}", "198.51.100.10", null, at));
        }
    }

    private async Task AddMonitorPointsAsync(CancellationToken ct)
    {
        // The fixed WEB-SRV-01 of 08 (192.168.1.10): generated devices of other locations may carry the same name.
        var web = await db.Set<Device>().AsNoTracking().Where(d => d.Name == "WEB-SRV-01" && d.LocalIp == "192.168.1.10").Join(db.Set<Tenant>().Where(t => t.Code == "ACME"), d => d.TenantId, t => t.Id, (d, _) => d)
            .FirstOrDefaultAsync(ct);
        if (web is null)
            return;
        var points = new (string Key, string Name, string Type, string Target, PointStatus Status, string Message, decimal? Ms)[]
        {
            ("ftp", "FTP Server", "Application", "FileZilla Server.exe", PointStatus.Healthy, "Running", null),
            ("db", "Database", "Database", "SQL Server (MSSQLSERVER)", PointStatus.Healthy, "Connected", 14m),
            ("iis", "IIS", "Website", "http://localhost/health", PointStatus.Warning, "Slow response (2.4 s)", 2400m),
            ("services", "Windows Services", "Service", "W3SVC, MSSQLSERVER, Spooler", PointStatus.Healthy, "All running", null),
            ("disk-c", "Disk C:", "Disk", "C:", PointStatus.Critical, "92% used", null),
            ("network", "Network", "Network", "192.168.1.1", PointStatus.Healthy, "0% packet loss", 3m),
        };
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var point = MonitorPoint.FromAgent(web.TenantId, web.Id, p.Key, p.Name, p.Type, p.Target, true, 60, i);
            var state = MonitorPointState.Create(point.Id, web.TenantId, web.Id);
            state.Update(p.Status, p.Message, p.Ms, now.AddSeconds(-random.Next(5, 55)), now.AddHours(-random.Next(1, 30)));
            db.AddRange(point, state);
        }
    }

    private async Task AddRecipientsAsync(IReadOnlyList<Tenant> tenants, CancellationToken ct)
    {
        var cairo = await db.Set<Location>().AsNoTracking().Where(l => l.Code == "CAIRO-HQ").Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        foreach (var tenant in tenants)
        {
            var domain = $"{tenant.Code.ToLowerInvariant()}.test";
            db.Set<AlertRecipient>().Add(AlertRecipient.Create(tenant.Id, "IT Operations", $"it@{domain}", RecipientEvents.WarningsAndCritical, null));
            if (tenant.Code == "ACME")
            {
                db.Set<AlertRecipient>().Add(AlertRecipient.Create(tenant.Id, "Administrator", $"admin@{domain}", RecipientEvents.CriticalOnly, null));
                if (cairo is { } id)
                    db.Set<AlertRecipient>().Add(AlertRecipient.Create(tenant.Id, "Cairo HQ desk", $"tech@{domain}", RecipientEvents.All, id));
            }
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateOnly LocalDay(DateTimeOffset at, string timeZone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    }
}
