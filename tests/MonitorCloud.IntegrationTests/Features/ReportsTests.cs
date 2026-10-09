using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Reports;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Reports;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Storage;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Helpers shared by the report tests: request, generate with the job, download and parse the CSV.</summary>
internal static class ReportFlow
{
    public static async Task<ReportDto> GenerateAsync(TestApp app, HttpClient client, object body)
    {
        using var response = await client.PostJsonAsync("/api/v1/reports", body);
        var queued = await response.ShouldBeOkAsync<ReportDto>(HttpStatusCode.Accepted);
        queued.Status.ShouldBe("Queued");
        (await app.Services.GetRequiredService<ReportsJob>().RunOnceAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        var list = await (await client.GetAsync(new Uri("/api/v1/reports?pageSize=200", UriKind.Relative))).ShouldBeOkAsync<PagedResult<ReportDto>>();
        var done = list.Items.Single(r => r.Id == queued.Id);
        done.Status.ShouldBe("Done", done.Error);
        return done;
    }

    public static async Task<List<string[]>> DownloadCsvAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/reports/{id}/download", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "UTF-8 BOM for spreadsheet programs");
        return Parse(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    /// <summary>RFC 4180: quoted fields may contain commas, quotes ("") and line breaks.</summary>
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }
}

/// <summary>Report requests, the generation job, downloads and their scope rules (MC-901, MC-902).</summary>
[Collection(SqlCollection.Name)]
public sealed class ReportsTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    [Fact]
    public async Task Report_types_show_the_eight_types_with_the_plan_entitlement()
    {
        using var enterprise = App.ClientFor(World.A.ReportViewer);
        var a = await (await enterprise.GetAsync(new Uri("/api/v1/reports/types", UriKind.Relative))).ShouldBeOkAsync<ReportTypesDto>();
        a.Types.Select(t => t.Type).ShouldBe(["overview", "location-summary", "device-health", "performance", "network-usage", "alerts", "license-usage", "custom"]);
        a.Types.ShouldAllBe(t => t.Entitled);

        using var starter = App.ClientFor(World.B.ReportViewer);
        var b = await (await starter.GetAsync(new Uri("/api/v1/reports/types", UriKind.Relative))).ShouldBeOkAsync<ReportTypesDto>();
        b.Types.Where(t => !t.Entitled).Select(t => t.Type).ShouldBe(["performance", "network-usage", "custom"]);
    }

    [Fact]
    public async Task A_requested_report_is_generated_by_the_job_and_downloads_as_csv()
    {
        using var client = App.ClientFor(World.A.ReportViewer);

        var report = await ReportFlow.GenerateAsync(App, client, new { type = "device-health", format = "csv" });

        report.SizeBytes.ShouldBeGreaterThan(0);
        var rows = await ReportFlow.DownloadCsvAsync(client, report.Id);
        rows[0].ShouldBe(["Device", "Location", "Operating system", "Connection", "Health", "Licence", "CPU %", "RAM %", "Disk %", "Open alerts", "Last seen (UTC)"]);
        rows.Skip(1).Select(r => r[0]).ShouldBe(["ALPHA-PC-01", "ALPHA-PC-02"]);
        (await App.InDbAsync(db => db.Set<AuditRecord>().AnyAsync(a => a.Action == "report.requested"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.PostJsonAsync("/api/v1/reports", new { type = "nope" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await client.PostJsonAsync("/api/v1/reports", new { type = "overview", format = "xlsx" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var now = App.Clock.GetUtcNow();
        await (await client.PostJsonAsync("/api/v1/reports", new { type = "overview", from = now, to = now.AddDays(-1) })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await client.PostJsonAsync("/api/v1/reports", new { type = "overview", locationIds = new[] { World.B.Location1.Id } }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
    }

    [Fact]
    public async Task Advanced_reports_need_the_advanced_feature()
    {
        using var starter = App.ClientFor(World.B.Administrator);

        await (await starter.PostJsonAsync("/api/v1/reports", new { type = "performance" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
        (await starter.PostJsonAsync("/api/v1/reports", new { type = "overview" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_queued_report_cannot_be_downloaded_yet()
    {
        using var client = App.ClientFor(World.A.Administrator);
        using var response = await client.PostJsonAsync("/api/v1/reports", new { type = "overview" });
        var queued = await response.ShouldBeOkAsync<ReportDto>(HttpStatusCode.Accepted);

        await (await client.GetAsync(new Uri($"/api/v1/reports/{queued.Id}/download", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "REPORT_NOT_READY");
    }

    [Fact]
    public async Task A_location_restricted_user_gets_only_their_locations_and_their_own_reports()
    {
        using var restricted = App.ClientFor(World.A.RestrictedManager);

        await (await restricted.PostJsonAsync("/api/v1/reports", new { type = "overview", locationIds = new[] { World.A.Location2.Id } }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
        var report = await ReportFlow.GenerateAsync(App, restricted, new { type = "device-health" });
        var rows = await ReportFlow.DownloadCsvAsync(restricted, report.Id);
        rows.Skip(1).Select(r => r[0]).ShouldBe(["ALPHA-PC-01"], "Device2 is in Location2, outside the user's scope");

        // The fixture's tenant-wide report is not theirs.
        var list = await (await restricted.GetAsync(new Uri("/api/v1/reports", UriKind.Relative))).ShouldBeOkAsync<PagedResult<ReportDto>>();
        list.Items.Select(r => r.Id).ShouldBe([report.Id]);
        await (await restricted.GetAsync(new Uri($"/api/v1/reports/{World.A.Archive.Report1}/download", UriKind.Relative)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "REPORT_NOT_FOUND");
    }

    [Fact]
    public async Task Csv_neutralises_text_that_a_spreadsheet_would_run_as_a_formula()
    {
        await App.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MonitorCloud.Infrastructure.Persistence.AppDbContext>();
            var device = await db.Set<MonitorCloud.Domain.Devices.Device>().SingleAsync(d => d.Id == World.A.Device1.Id);
            device.Rename("=HYPERLINK(\"https://evil.test\")");
            await db.SaveChangesAsync();
        });
        using var client = App.ClientFor(World.A.Administrator);

        var report = await ReportFlow.GenerateAsync(App, client, new { type = "device-health" });
        var rows = await ReportFlow.DownloadCsvAsync(client, report.Id);

        rows.Skip(1).Select(r => r[0]).ShouldContain("'=HYPERLINK(\"https://evil.test\")");
    }

    [Fact]
    public void Csv_and_html_escape_their_values()
    {
        var data = new ReportData("T <b>", App.Clock.GetUtcNow(), App.Clock.GetUtcNow(), [new("Devices", "1")],
            [new("name", "Name"), new("n", "N", true)], [["a,\"b\"", 1.5m], ["-1", -2m]]);

        var csv = Encoding.UTF8.GetString(ReportRenderer.Csv(data)[3..]);
        ReportFlow.Parse(csv).ShouldBe([["Name", "N"], ["a,\"b\"", "1.5"], ["'-1", "-2"]]);
        ReportRenderer.Html(data, "Alpha & Co").ShouldContain("T &lt;b&gt;");
        ReportRenderer.Html(data, "Alpha & Co").ShouldContain("Alpha &amp; Co");
    }

    [Fact]
    public async Task Platform_reports_cover_every_customer()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        var data = await (await client.GetAsync(new Uri("/api/v1/platform/reports/license-usage", UriKind.Relative))).ShouldBeOkAsync<ReportData>();

        data.Columns[0].Key.ShouldBe("customer");
        data.Rows.Select(r => r[0]!.ToString()).ShouldBe([World.A.Tenant.Name, World.B.Tenant.Name]);
        await (await client.GetAsync(new Uri("/api/v1/platform/reports/nope", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        using var tenant = App.ClientFor(World.A.Administrator);
        (await tenant.GetAsync(new Uri("/api/v1/platform/reports/overview", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_failing_report_is_marked_failed_with_the_reason()
    {
        await App.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<MonitorCloud.Infrastructure.Persistence.AppDbContext>();
            db.Add(GeneratedReport.Request(World.A.Id, "overview", "Broken", "not json", ReportFormat.Csv, World.A.Administrator.Id, App.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        });

        (await App.Services.GetRequiredService<ReportsJob>().RunOnceAsync(CancellationToken.None)).ShouldBe(1);

        var broken = await App.InDbAsync(db => db.Set<GeneratedReport>().IgnoreQueryFilters().SingleAsync(r => r.Title == "Broken"));
        broken.Status.ShouldBe(ReportStatus.Failed);
        broken.Error.ShouldNotBeNullOrEmpty();
    }
}

/// <summary>MC-901 acceptance: every number of the eight reports equals an independent SQL query over the demo seed.</summary>
[Collection(SqlCollection.Name)]
public sealed class ReportNumbersTests(SeededDemoFixture fixture) : IClassFixture<SeededDemoFixture>
{
    private TestApp App => fixture.App;

    private const string Active = "FROM devices.DeviceStates s JOIN devices.Devices d ON d.Id = s.DeviceId WHERE d.Status = 'Active'";

    private sealed record Group(string Name, int Devices, int Online, int Healthy, int Warning, int Critical, int Licensed);

    private sealed record DeviceNumbers(string Name, string Location, decimal? CpuAvg, decimal? CpuMax, decimal? RamMax, long Rx, long Tx);

    private async Task<(HttpClient Client, Guid Tenant)> AcmeAsync()
    {
        var user = await App.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == "admin@acme.test"));
        return (App.ClientFor(user), user.TenantId!.Value);
    }

    private (DateTimeOffset From, DateTimeOffset To) Period()
    {
        var to = App.Clock.GetUtcNow();
        return (to.AddDays(-7), to);
    }

    private string Between(string column)
    {
        var (from, to) = Period();
        return $"{column} >= CAST('{from:O}' AS datetimeoffset) AND {column} < CAST('{to:O}' AS datetimeoffset)";
    }

    private Task<int> CountAsync(string sql) => App.InDbAsync(db => db.Database.SqlQueryRaw<int>(sql).SingleAsync());

    private Task<List<Group>> GroupsAsync(Guid tenant)
    {
        var sql = $"""
        SELECT l.Name, COUNT(*) AS Devices, SUM(CASE WHEN s.Connection = 1 THEN 1 ELSE 0 END) AS Online, SUM(CASE WHEN s.Health = 1 THEN 1 ELSE 0 END) AS Healthy,
               SUM(CASE WHEN s.Health = 2 THEN 1 ELSE 0 END) AS Warning, SUM(CASE WHEN s.Health = 3 THEN 1 ELSE 0 END) AS Critical,
               SUM(CASE WHEN s.LicenseState = 1 THEN 1 ELSE 0 END) AS Licensed
        {Active} AND s.TenantId = '{tenant}'
        GROUP BY l.Name
        """.Replace("FROM devices.DeviceStates s JOIN devices.Devices d ON d.Id = s.DeviceId",
            "FROM devices.DeviceStates s JOIN devices.Devices d ON d.Id = s.DeviceId JOIN tenancy.Locations l ON l.Id = s.LocationId", StringComparison.Ordinal);
        return App.InDbAsync(db => db.Database.SqlQueryRaw<Group>(sql).ToListAsync());
    }

    private static int Int(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static decimal Dec(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<List<string[]>> CsvAsync(string type, object? extra = null)
    {
        var (client, _) = await AcmeAsync();
        using (client)
        {
            var (from, to) = Period();
            var body = new Dictionary<string, object?> { ["type"] = type, ["from"] = from, ["to"] = to };
            if (extra is not null)
            {
                foreach (var p in extra.GetType().GetProperties())
                    body[p.Name] = p.GetValue(extra);
            }

            var report = await ReportFlow.GenerateAsync(App, client, body);
            return await ReportFlow.DownloadCsvAsync(client, report.Id);
        }
    }

    [Fact]
    public async Task Overview_counts_devices_and_alerts_per_location()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("overview");
        var groups = await GroupsAsync(acme);

        rows[0].ShouldBe(["Location", "Devices", "Online", "Healthy", "Warning", "Critical", "Alerts"]);
        rows.Count.ShouldBe(groups.Count + 1);
        foreach (var g in groups)
        {
            var row = rows.Single(r => r[0] == g.Name);
            (Int(row[1]), Int(row[2]), Int(row[3]), Int(row[4]), Int(row[5])).ShouldBe((g.Devices, g.Online, g.Healthy, g.Warning, g.Critical), g.Name);
            var alerts = await CountAsync($"SELECT COUNT(*) AS Value FROM monitoring.Alerts a JOIN tenancy.Locations l ON l.Id = a.LocationId WHERE a.TenantId = '{acme}' AND l.Name = N'{g.Name.Replace("'", "''", StringComparison.Ordinal)}' AND {Between("a.FirstSeenAt")}");
            Int(row[6]).ShouldBe(alerts, g.Name);
        }

        rows.Skip(1).Sum(r => Int(r[1])).ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.TenantId = '{acme}'"));
    }

    [Fact]
    public async Task Location_summary_counts_offline_licensed_and_the_health_score()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("location-summary");
        var groups = await GroupsAsync(acme);

        rows.Count.ShouldBe(groups.Count + 1);
        foreach (var g in groups)
        {
            var row = rows.Single(r => r[0] == g.Name);
            (Int(row[2]), Int(row[3]), Int(row[4]), Int(row[5]), Int(row[6]), Int(row[7]), Int(row[8]))
                .ShouldBe((g.Devices, g.Online, g.Devices - g.Online, g.Healthy, g.Warning, g.Critical, g.Licensed), g.Name);
            Dec(row[9]).ShouldBe(Math.Round(100m * g.Healthy / g.Devices, 1), g.Name);
        }
    }

    [Fact]
    public async Task Device_health_lists_every_active_device_with_its_open_alerts()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("device-health");

        rows.Count.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.TenantId = '{acme}'") + 1);
        rows.Skip(1).Sum(r => Int(r[9])).ShouldBe(await CountAsync($"SELECT SUM(s.OpenCritical + s.OpenWarning) AS Value {Active} AND s.TenantId = '{acme}'"));
        rows.Skip(1).Count(r => r[4] == "Critical").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.TenantId = '{acme}' AND s.Health = 3"));
    }

    [Fact]
    public async Task Performance_uses_sample_weighted_hourly_averages()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("performance");
        var expected = await DeviceNumbersAsync(acme);

        rows[0].ShouldBe(["Device", "Location", "CPU avg %", "CPU max %", "RAM avg %", "RAM max %", "Disk max %", "Hours"]);
        expected.Count(e => e.CpuAvg is not null).ShouldBeGreaterThan(0, "the seed has hourly telemetry");
        foreach (var e in expected)
        {
            var row = rows.Single(r => r[0] == e.Name && r[1] == e.Location);
            if (e.CpuAvg is null)
            {
                row[2].ShouldBeEmpty();
                continue;
            }

            Math.Abs(Dec(row[2]) - e.CpuAvg.Value).ShouldBeLessThanOrEqualTo(0.01m, e.Name);
            Dec(row[3]).ShouldBe(e.CpuMax!.Value, e.Name);
            Dec(row[5]).ShouldBe(e.RamMax!.Value, e.Name);
        }
    }

    [Fact]
    public async Task Network_usage_sums_hourly_bytes()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("network-usage");
        var expected = await DeviceNumbersAsync(acme);

        rows[0].ShouldBe(["Device", "Location", "Download GB", "Upload GB", "Total GB"]);
        foreach (var e in expected)
        {
            var row = rows.Single(r => r[0] == e.Name && r[1] == e.Location);
            Dec(row[2]).ShouldBe(Math.Round(e.Rx / 1_000_000_000m, 2), e.Name);
            Dec(row[3]).ShouldBe(Math.Round(e.Tx / 1_000_000_000m, 2), e.Name);
        }

        expected.Sum(e => e.Rx).ShouldBeGreaterThan(0, "the seed has network telemetry");
    }

    [Fact]
    public async Task Alerts_lists_every_alert_opened_in_the_period()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("alerts");
        var where = $"FROM monitoring.Alerts WHERE TenantId = '{acme}' AND {Between("FirstSeenAt")}";

        rows.Count.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where}") + 1);
        rows.Skip(1).Count(r => r[1] == "Critical").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND Severity = 'Critical'"));
        rows.Skip(1).Count(r => r[6] == "Resolved").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND Status = 'Resolved'"));
        rows.Skip(1).Sum(r => Int(r[8])).ShouldBe(await CountAsync($"SELECT ISNULL(SUM(Occurrences), 0) AS Value {where}"));
    }

    [Fact]
    public async Task License_usage_counts_licensed_devices_by_operating_system()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("license-usage");

        rows[0].ShouldBe(["Operating system", "Licensed devices", "% of licensed"]);
        var licensed = $"{Active} AND s.TenantId = '{acme}' AND s.LicenseState = 1";
        rows.Skip(1).Sum(r => Int(r[1])).ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {licensed}"));
        Int(rows.Single(r => r[0] == "Windows")[1]).ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {licensed} AND s.OsFamily = 1"));
    }

    [Fact]
    public async Task Custom_report_counts_the_period_alerts_of_each_device()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("custom");

        rows.Count.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.TenantId = '{acme}'") + 1);
        rows.Skip(1).Sum(r => Int(r[7])).ShouldBe(await CountAsync(
            $"SELECT COUNT(*) AS Value FROM monitoring.Alerts a JOIN devices.Devices d ON d.Id = a.DeviceId WHERE d.Status = 'Active' AND a.TenantId = '{acme}' AND {Between("a.FirstSeenAt")}"));
    }

    [Fact]
    public async Task Performance_grouped_by_location_has_one_row_per_location()
    {
        var (_, acme) = await AcmeAsync();
        var rows = await CsvAsync("performance", new { groupBy = "location" });

        rows[0][0].ShouldBe("Location");
        rows.Count.ShouldBe((await GroupsAsync(acme)).Count + 1);
    }

    private Task<List<DeviceNumbers>> DeviceNumbersAsync(Guid tenant)
    {
        // MetricHours buckets are UTC datetime2 values.
        var (from, to) = Period();
        var hours = $"h.BucketUtc >= '{from.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffff}' AND h.BucketUtc < '{to.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffff}'";
        var sql = $"""
            SELECT d.Name, l.Name AS Location,
                   CAST(ROUND(SUM(h.CpuAvg * h.Samples) / NULLIF(SUM(CAST(h.Samples AS decimal(18, 4))), 0), 2) AS decimal(9, 2)) AS CpuAvg,
                   MAX(h.CpuMax) AS CpuMax, MAX(h.RamMax) AS RamMax,
                   ISNULL(SUM(COALESCE(h.NetRxBytes, ISNULL(h.NetRxBps, 0) * 3600)), 0) AS Rx,
                   ISNULL(SUM(COALESCE(h.NetTxBytes, ISNULL(h.NetTxBps, 0) * 3600)), 0) AS Tx
            FROM devices.DeviceStates s
            JOIN devices.Devices d ON d.Id = s.DeviceId
            JOIN tenancy.Locations l ON l.Id = s.LocationId
            LEFT JOIN telemetry.MetricHours h ON h.DeviceId = d.Id AND {hours}
            WHERE d.Status = 'Active' AND s.TenantId = '{tenant}'
            GROUP BY d.Id, d.Name, l.Name
            """;
        return App.InDbAsync(db => db.Database.SqlQueryRaw<DeviceNumbers>(sql).ToListAsync());
    }
}