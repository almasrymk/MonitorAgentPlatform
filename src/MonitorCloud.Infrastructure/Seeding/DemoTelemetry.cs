using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using MonitorCloud.Infrastructure.Devices;

namespace MonitorCloud.Infrastructure.Seeding;

/// <summary>
/// Part 3 of the demo seed (MC-506, 08 section 4): hourly metrics for 7 days for the detailed customers, minutes for
/// the last 6 hours in Acme / Cairo HQ, disk usage for the fixed devices (C: of SQL-DB-01 filling steadily) and one
/// snapshot per online Cairo HQ device. Values: base load by device role, a daily sine curve, noise, and plateaus that
/// match open alerts (WEB-SRV-01 CPU at 92% for the last 20 minutes).
/// </summary>
internal sealed class DemoTelemetry(string connectionString, DateTimeOffset now, Random random)
{
    private sealed record DeviceRow(Guid Id, Guid Tenant, string Name, string TenantCode, string LocationCode, byte Connection, byte Health, decimal? CpuPercent, decimal? RamPercent, decimal? DiskPercent, string? LocalIp);

    private static readonly string[] Detailed = ["ACME", "NILE", "DELTA", "HORIZON", "SAHARA", "GULF", "PYRAMID", "OASIS"];
    private static readonly string[] FixedNames = ["WEB-SRV-01", "DB-SRV-01", "APP-SRV-02", "DESK-01", "BR-DC-01", "SQL-DB-01", "DEV-MAC-01", "FILE-SRV-01"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int Rows { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var devices = (await connection.QueryAsync<DeviceRow>("""
            SELECT d.Id, d.TenantId AS Tenant, d.Name, t.Code AS TenantCode, l.Code AS LocationCode, s.Connection, s.Health, s.CpuPercent, s.RamPercent, s.DiskPercent, d.LocalIp
            FROM devices.Devices d
            JOIN devices.DeviceStates s ON s.DeviceId = d.Id
            JOIN tenancy.Tenants t ON t.Id = d.TenantId
            JOIN tenancy.Locations l ON l.Id = d.LocationId
            WHERE t.Code IN @codes AND d.Status = 'Active'
            """, new { codes = Detailed })).ToList();

        var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var hours = NewMetricTable();
        foreach (var device in devices)
        {
            for (var h = 7 * 24; h >= 1; h--)
                AddRow(hours, device, hourStart.AddHours(-h), samples: 720, hourly: true);
        }

        await CopyAsync(connection, "telemetry.MetricHours", hours, cancellationToken);

        var cairo = devices.Where(d => d.TenantCode == "ACME" && d.LocationCode == "CAIRO-HQ").ToList();
        var minuteStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var minutes = NewMetricTable();
        foreach (var device in cairo.Where(d => d.Connection == 1))
        {
            for (var m = 6 * 60; m >= 1; m--)
                AddRow(minutes, device, minuteStart.AddMinutes(-m), samples: 12, hourly: false);
        }

        await CopyAsync(connection, "telemetry.MetricMinutes", minutes, cancellationToken);

        var disks = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("Drive", typeof(string)), ("BucketUtc", typeof(DateTime)), ("Label", typeof(string)), ("FileSystem", typeof(string)), ("TotalGb", typeof(decimal)), ("UsedGb", typeof(decimal)), ("FreeGb", typeof(decimal)) })
            disks.Columns.Add(name, type);
        foreach (var device in cairo.Where(d => FixedNames.Contains(d.Name)))
        {
            var partitions = device.Name == "WEB-SRV-01"
                ? new (string Drive, string Label, decimal Total, decimal Percent)[] { ("C:", "System", 250, 92), ("D:", "Data", 1000, 76), ("E:", "Backup", 2000, 45) }
                : [("C:", "System", 250, device.DiskPercent ?? 50)];
            for (var h = 7 * 24; h >= 0; h--)
            {
                foreach (var p in partitions)
                {
                    // SQL-DB-01's C: fills steadily over the week (08 section 4).
                    var percent = device.Name == "SQL-DB-01" && p.Drive == "C:" ? Math.Min(97, 70 + ((7 * 24) - h) * 0.15m) : p.Percent;
                    var used = Math.Round(p.Total * percent / 100, 2);
                    disks.Rows.Add(device.Id, device.Tenant, p.Drive, hourStart.AddHours(-h), p.Label, "NTFS", p.Total, used, p.Total - used);
                }
            }
        }

        await CopyAsync(connection, "telemetry.DiskUsageHours", disks, cancellationToken);

        var snapshots = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("Json", typeof(byte[])), ("CapturedAt", typeof(DateTimeOffset)) })
            snapshots.Columns.Add(name, type);
        foreach (var device in cairo.Where(d => d.Connection == 1))
            snapshots.Rows.Add(device.Id, device.Tenant, Snapshot(device), now.AddSeconds(-random.Next(5, 60)));
        await CopyAsync(connection, "telemetry.LiveSnapshots", snapshots, cancellationToken);
    }

    private static DataTable NewMetricTable()
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("DeviceId", typeof(Guid)), ("TenantId", typeof(Guid)), ("BucketUtc", typeof(DateTime)), ("Samples", typeof(short)), ("CpuAvg", typeof(decimal)), ("CpuMax", typeof(decimal)),
                     ("CpuP95", typeof(decimal)), ("RamAvg", typeof(decimal)), ("RamMax", typeof(decimal)), ("DiskActiveAvg", typeof(decimal)), ("DiskReadBps", typeof(long)), ("DiskWriteBps", typeof(long)),
                     ("DiskResponseMs", typeof(decimal)), ("NetRxBps", typeof(long)), ("NetTxBps", typeof(long)), ("NetRxBytes", typeof(long)), ("NetTxBytes", typeof(long)), ("PingMs", typeof(decimal)),
                     ("PacketLossPercent", typeof(decimal)), ("TempMaxC", typeof(decimal)), ("DiskPercentMax", typeof(decimal)),
                 })
            table.Columns.Add(name, type);
        return table;
    }

    private void AddRow(DataTable table, DeviceRow device, DateTime bucket, short samples, bool hourly)
    {
        var baseCpu = BaseCpu(device);
        var day = Math.Sin(((bucket.TimeOfDay.TotalHours - 9) / 24) * 2 * Math.PI);
        var cpu = Math.Clamp(baseCpu + (day * 10) + ((random.NextDouble() - 0.5) * 8), 1, 99);
        // The current values of the device (its state) hold for the last 20 minutes, so busy devices show their plateau.
        if (!hourly && device.CpuPercent is { } current && bucket >= now.UtcDateTime.AddMinutes(-20))
            cpu = (double)current + ((random.NextDouble() - 0.5) * 2);
        var ram = Math.Clamp((double)(device.RamPercent ?? 50) + (day * 3) + ((random.NextDouble() - 0.5) * 4), 5, 99);
        var rx = (long)(80_000 + random.Next(0, 400_000));
        var tx = (long)(20_000 + random.Next(0, 120_000));
        var seconds = hourly ? 3600 : 60;
        table.Rows.Add(device.Id, device.Tenant, bucket, samples, D(cpu), D(Math.Min(100, cpu + random.Next(3, 12))), D(Math.Min(100, cpu + random.Next(2, 8))),
            D(ram), D(Math.Min(100, ram + random.Next(1, 4))), D(2 + (random.NextDouble() * 6)), (long)random.Next(10_000, 200_000), (long)random.Next(50_000, 400_000),
            Math.Round((decimal)(2 + (random.NextDouble() * 8)), 2), rx, tx, rx * seconds / 8, tx * seconds / 8, Math.Round((decimal)(12 + (random.NextDouble() * 30)), 2), 0m,
            Math.Round((decimal)(45 + (random.NextDouble() * 15)), 1), device.DiskPercent ?? 50m);
        Rows++;
    }

    private static double BaseCpu(DeviceRow device) => device.Name switch
    {
        _ when device.Name.StartsWith("WEB-SRV", StringComparison.Ordinal) || device.Name.StartsWith("SQL-DB", StringComparison.Ordinal) => 45,
        _ when device.Name.StartsWith("DB-SRV", StringComparison.Ordinal) || device.Name.StartsWith("APP-SRV", StringComparison.Ordinal) => 35,
        _ when device.Name.StartsWith("DC", StringComparison.Ordinal) || device.Name.StartsWith("BR-DC", StringComparison.Ordinal) || device.Name.StartsWith("FILE-SRV", StringComparison.Ordinal) => 22,
        _ => 15,
    };

    private static decimal D(double value) => Math.Round((decimal)value, 2);

    private byte[] Snapshot(DeviceRow device)
    {
        var cpu = (double)(device.CpuPercent ?? 20);
        var ram = (double)(device.RamPercent ?? 40);
        string[] names = device.Name.StartsWith("SQL", StringComparison.Ordinal) || device.Name.StartsWith("DB", StringComparison.Ordinal)
            ? ["sqlservr.exe", "MsMpEng.exe", "svchost.exe", "SQLAGENT.EXE", "explorer.exe"]
            : ["w3wp.exe", "MsMpEng.exe", "svchost.exe", "MonitorAgent.exe", "explorer.exe"];
        object Top(double total, int scale) => names.Select((n, i) => new { name = n, pid = 1000 + (i * 211) + random.Next(0, 90), value = Math.Round(total * scale / (i + 2) / 4, 1) }).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            capturedAt = now,
            cpu = new { usage = cpu, physicalCores = 8, logicalCores = 16, speedGhz = 2.4, maxSpeedGhz = 2.8, tempC = 48 + random.Next(0, 12), processes = 180 + random.Next(0, 120), model = "Intel Xeon Silver 4314" },
            ram = new { usage = ram, totalGb = 31.7, usedGb = Math.Round(31.7 * ram / 100, 1), freeGb = Math.Round(31.7 * (100 - ram) / 100, 1), cachedGb = 2.1 },
            diskActivity = new { activePercent = random.Next(1, 9), readBps = random.Next(0, 90_000), writeBps = random.Next(20_000, 200_000), responseMs = 5.5 },
            partitions = new[] { new { drive = "C:", label = "System", fileSystem = "NTFS", totalGb = 250, usedGb = 250 * (double)(device.DiskPercent ?? 50) / 100, freeGb = 250 * (100 - (double)(device.DiskPercent ?? 50)) / 100, usage = device.DiskPercent ?? 50 } },
            network = new { adapter = "Intel(R) Ethernet", type = "Ethernet", downloadBps = random.Next(10_000, 600_000), uploadBps = random.Next(5_000, 200_000), publicIp = "203.0.113.10", localIp = device.LocalIp, pingMs = 22, lossPercent = 0, lastSpeedTest = new { downloadMbps = 18.2, uploadMbps = 9.1, at = now.AddHours(-5) } },
            top = new { cpu = Top(cpu, 4), ram = Top(ram * 80, 1), disk = Top(400, 1), network = Top(300, 1) },
            service = new { status = "Running", uptimeSeconds = 1_052_880 },
        }, Json);
        return InventoryCodec.Encode(json).Compressed;
    }

    private async Task CopyAsync(SqlConnection connection, string table, DataTable data, CancellationToken cancellationToken)
    {
        if (data.Rows.Count == 0)
            return;
        using var copy = new SqlBulkCopy(connection) { DestinationTableName = table, BatchSize = 10_000, BulkCopyTimeout = 600 };
        foreach (DataColumn column in data.Columns)
            copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await copy.WriteToServerAsync(data, cancellationToken);
    }
}
