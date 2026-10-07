using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorCloud.SimulatedAgent;

/// <summary>
/// Plausible values for a simulated device (05 section 11): a daily sine curve around a base load chosen by the device
/// seed, plus noise. The same fingerprint always produces the same curve.
/// </summary>
public sealed class MetricGenerator(string fingerprint)
{
    private readonly int _seed = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)), 0);

    private double BaseCpu => 15 + Math.Abs(_seed % 35);
    private double BaseRam => 35 + Math.Abs(_seed / 7 % 40);

    public (double Cpu, double Ram) Now(DateTimeOffset at)
    {
        var day = Math.Sin((at.UtcDateTime.TimeOfDay.TotalHours - 9) / 24 * 2 * Math.PI);
        var noise = new Random(_seed ^ (int)(at.ToUnixTimeSeconds() / 2));
        var cpu = Math.Clamp(BaseCpu + (day * 12) + ((noise.NextDouble() - 0.5) * 10), 1, 99);
        var ram = Math.Clamp(BaseRam + (day * 4) + ((noise.NextDouble() - 0.5) * 3), 5, 98);
        return (Math.Round(cpu, 1), Math.Round(ram, 1));
    }

    public MinuteAggregate Minute(DateTimeOffset bucket)
    {
        var start = new DateTimeOffset(bucket.Year, bucket.Month, bucket.Day, bucket.Hour, bucket.Minute, 0, TimeSpan.Zero);
        var (cpu, ram) = Now(start);
        return new MinuteAggregate
        {
            BucketStart = Timestamp.FromDateTimeOffset(start),
            Samples = 12,
            CpuAvg = cpu,
            CpuMax = Math.Min(100, cpu + 8),
            CpuP95 = Math.Min(100, cpu + 6),
            RamAvg = ram,
            RamMax = Math.Min(100, ram + 2),
            DiskActiveAvg = 4,
            DiskReadBps = 50_000,
            DiskWriteBps = 110_000,
            DiskResponseMs = 5.5,
            NetRxBps = 120_000,
            NetTxBps = 45_000,
            NetRxBytes = 7_200_000,
            NetTxBytes = 2_700_000,
            PingMs = 22,
            PacketLossPercent = 0,
            DiskPercentMax = 40 + Math.Abs(_seed / 13 % 45),
            UptimeSeconds = 86_400 + Math.Abs(_seed % 900_000),
        };
    }

    public DiskUsage Disks()
    {
        var used = 40 + Math.Abs(_seed / 13 % 45);
        return new DiskUsage { Drive = "C:", Label = "System", FileSystem = "NTFS", TotalGb = 250, UsedGb = 250.0 * used / 100, FreeGb = 250.0 * (100 - used) / 100 };
    }

    public byte[] Snapshot(DateTimeOffset at, string hostname)
    {
        var (cpu, ram) = Now(at);
        string[] processes = ["w3wp.exe", "sqlservr.exe", "MsMpEng.exe", "svchost.exe", "explorer.exe"];
        var top = processes.Select((name, i) => new { name, pid = 1000 + (i * 137), value = Math.Round(cpu / (i + 2), 1) }).ToArray();
        return Brotli(JsonSerializer.Serialize(new
        {
            capturedAt = at,
            cpu = new { usage = cpu, physicalCores = 8, logicalCores = 16, speedGhz = 2.4, maxSpeedGhz = 2.8, tempC = 52, processes = 248, model = "Intel Xeon Silver 4314" },
            ram = new { usage = ram, totalGb = 31.7, usedGb = Math.Round(31.7 * ram / 100, 1), freeGb = Math.Round(31.7 * (100 - ram) / 100, 1), cachedGb = 2.1 },
            diskActivity = new { activePercent = 4, readBps = 50_000, writeBps = 110_592, responseMs = 5.5 },
            partitions = new[] { new { drive = "C:", label = "System", fileSystem = "NTFS", totalGb = 250, usedGb = 160, freeGb = 90, usage = 64 } },
            network = new { adapter = "Ethernet0", type = "Ethernet", downloadBps = 120_000, uploadBps = 45_000, publicIp = "203.0.113.20", localIp = "10.20.0.10", pingMs = 22, lossPercent = 0 },
            top = new { cpu = top, ram = top, disk = top, network = top },
            service = new { status = "Running", uptimeSeconds = 1_052_880 },
            hostname,
        }, Json));
    }

    public (byte[] Bytes, string Hash) Inventory(InventoryKind kind, string hostname)
    {
        var json = JsonSerializer.Serialize(new { kind = kind.ToString(), hostname, items = Enumerable.Range(1, 5).Select(i => new { name = $"{kind} item {i}" }) }, Json);
        return (Brotli(json), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static byte[] Brotli(string json)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(Encoding.UTF8.GetBytes(json));
        return output.ToArray();
    }
}