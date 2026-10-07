using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Net.Client;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.SimulatedAgent;

/// <summary>
/// <c>simulator load --devices 1500 --duration 10m --report docs/perf/load.json</c> (09 section 8): enrolls the devices
/// over the demo customers with free seats, keeps all streams open with heartbeats, one metric batch per device and
/// minute (spread over the minute) and a snapshot, and measures acknowledgement latency, data loss, API latency of the
/// device list and the dashboard, and the server's memory.
/// </summary>
internal static class LoadTest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(HttpClient http, AgentCloudClient cloud, Store store, Options options, int devices, TimeSpan duration, string reportPath, string? serverProcess)
    {
        var started = DateTimeOffset.UtcNow;
        Console.WriteLine($"Load test: {devices} devices for {duration}.");
        var enrolled = await EnrollAsync(cloud, store, options, devices);
        Console.WriteLine($"{enrolled.Count} devices enrolled in {(DateTimeOffset.UtcNow - started).TotalSeconds:0} s.");

        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan, KeepAlivePingDelay = TimeSpan.FromSeconds(60) };
        using var channel = GrpcChannel.ForAddress(options.Gateway ?? enrolled[0].GatewayUrl, new GrpcChannelOptions { HttpHandler = handler });
        using var stop = new CancellationTokenSource(duration);
        var agents = new ConcurrentBag<SimulatedAgent>();
        var batchesSent = 0;
        var connectFailures = 0;

        var runs = enrolled.Select(async (device, index) =>
        {
            // Spread the connections and the metric batches over the first minute.
            var offset = TimeSpan.FromMilliseconds(60_000.0 * index / enrolled.Count);
            await Task.Delay(offset);
            var identity = new AgentIdentity(device.DeviceId, device.DeviceSecret, device.Fingerprint, device.Hostname, device.GatewayUrl);
            var agent = new SimulatedAgent(identity, channel);
            agents.Add(agent);
            try
            {
                var token = await cloud.TokenAsync(identity, stop.Token);
                if (await agent.ConnectAsync(token, cancellationToken: stop.Token) is null)
                {
                    Interlocked.Increment(ref connectFailures);
                    return;
                }

                var heartbeats = agent.RunHeartbeatsAsync(stop.Token);
                while (!stop.IsCancellationRequested && !agent.Completion.IsCompleted)
                {
                    await agent.SendMetricsAsync(DateTimeOffset.UtcNow.AddMinutes(-1), withDisks: false, cancellationToken: stop.Token);
                    await agent.SnapshotAsync(stop.Token);
                    Interlocked.Increment(ref batchesSent);
                    await Task.Delay(TimeSpan.FromSeconds(60), stop.Token);
                }

                await heartbeats;
            }
            catch (Exception ex) when (ex is OperationCanceledException or Grpc.Core.RpcException or InvalidOperationException or HttpRequestException)
            {
                // The run ended or the device failed: counted below.
            }
        }).ToList();

        var api = MeasureApiAsync(http, options, stop.Token);
        var memory = SampleMemoryAsync(serverProcess, stop.Token);
        await Task.WhenAll(runs);

        // Give the last batches time to be acknowledged before counting losses.
        await Task.Delay(TimeSpan.FromSeconds(10));
        var latencies = agents.SelectMany(a => a.AckLatenciesMs).Order().ToList();
        var unacknowledged = agents.Sum(a => a.Unacknowledged);
        var (listMs, dashboardMs) = await api;
        var memorySamples = await memory;
        foreach (var agent in agents)
        {
            try
            {
                await agent.GoodbyeAsync(GoodbyeReason.ServiceStopping);
            }
            catch (Exception ex) when (ex is Grpc.Core.RpcException or InvalidOperationException)
            {
                // Already closed.
            }

            await agent.DisposeAsync();
        }

        var report = new JsonObject
        {
            ["startedAt"] = started,
            ["devices"] = enrolled.Count,
            ["durationSeconds"] = duration.TotalSeconds,
            ["connectFailures"] = connectFailures,
            ["metricBatchesSent"] = batchesSent,
            ["acknowledged"] = latencies.Count,
            ["unacknowledged"] = unacknowledged,
            ["ackLatencyMs"] = Percentiles(latencies),
            ["deviceListMs"] = Percentiles(listMs),
            ["dashboardMs"] = Percentiles(dashboardMs),
            ["serverWorkingSetMb"] = new JsonArray([.. memorySamples.Select(m => JsonValue.Create(m))]),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToJsonString(Json));
        Console.WriteLine(report.ToJsonString(Json));
        return 0;
    }

    private static JsonObject Percentiles(List<double> values)
    {
        double P(double p) => values.Count == 0 ? 0 : Math.Round(values[Math.Clamp((int)Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1)], 1);
        values.Sort();
        return new JsonObject { ["count"] = values.Count, ["p50"] = P(0.5), ["p95"] = P(0.95), ["p99"] = P(0.99), ["max"] = values.Count == 0 ? 0 : Math.Round(values[^1], 1) };
    }

    /// <summary>Samples the device list (24 items) and the customer dashboard as the Acme administrator every 2 s.</summary>
    private static async Task<(List<double> List, List<double> Dashboard)> MeasureApiAsync(HttpClient http, Options options, CancellationToken cancellationToken)
    {
        var list = new List<double>();
        var dashboard = new List<double>();
        using var login = await http.PostAsJsonAsync(new Uri("api/v1/auth/login", UriKind.Relative), new { email = $"admin@{options.Tenant}.test", password = options.AdminPassword }, cancellationToken);
        if (!login.IsSuccessStatusCode)
            return (list, dashboard);
        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync(cancellationToken))!["accessToken"]!.GetValue<string>();
        var issued = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - issued > TimeSpan.FromMinutes(12))
                    return (list, dashboard);
                list.Add(await TimeAsync(http, token, "api/v1/devices?pageSize=24", cancellationToken));
                dashboard.Add(await TimeAsync(http, token, "api/v1/dashboard", cancellationToken));
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return (list, dashboard);
    }

    private static async Task<double> TimeAsync(HttpClient http, string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var watch = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, cancellationToken);
        await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return watch.Elapsed.TotalMilliseconds;
    }

    private static async Task<List<double>> SampleMemoryAsync(string? processName, CancellationToken cancellationToken)
    {
        var samples = new List<double>();
        if (string.IsNullOrWhiteSpace(processName))
            return samples;
        while (!cancellationToken.IsCancellationRequested)
        {
            var process = Process.GetProcessesByName(processName).FirstOrDefault();
            if (process is not null)
                samples.Add(Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return samples;
    }

    /// <summary>Enrolls the missing devices over the customers whose licence has free seats (fake data of the demo seed).</summary>
    private static async Task<List<StoredDevice>> EnrollAsync(AgentCloudClient cloud, Store store, Options options, int wanted)
    {
        var mine = store.Devices.Where(d => d.Tenant == "load").Take(wanted).ToList();
        if (mine.Count >= wanted)
            return mine;
        var data = JsonNode.Parse(await File.ReadAllTextAsync(options.LicensingPath))!;
        var subscriptions = data["subscriptions"]!.AsArray().ToDictionary(s => s!["id"]!.GetValue<Guid>(), s => s!["status"]!.GetValue<string>());
        var free = data["licenses"]!.AsArray()
            .Where(l => subscriptions.GetValueOrDefault(l!["subscriptionId"]!.GetValue<Guid>()) == "Active" && l!["status"]!.GetValue<string>() == "Active")
            .Select(l => (Key: l!["productKey"]!.GetValue<string>(), Free: (l["maxActivations"]?.GetValue<int?>() ?? 1000) - l["devices"]!.AsArray().Count - 5))
            .Where(l => l.Free > 0)
            .ToList();
        var startCount = mine.Count;
        var slots = free.SelectMany(l => Enumerable.Repeat(l.Key, l.Free)).ToList();
        if (slots.Count < wanted - startCount)
            throw new InvalidOperationException($"Only {slots.Count} free seats in the demo licences.");

        var start = mine.Count;
        using var gate = new SemaphoreSlim(16);
        var lockObject = new object();
        var exhausted = new HashSet<string>(StringComparer.Ordinal);
        var next = start;
        var tasks = Enumerable.Range(start + 1, wanted - start).Select(async n =>
        {
            await gate.WaitAsync();
            try
            {
                var fingerprint = $"sim-load-{options.RunId ?? "run"}-{n:00000}";
                // The seed file shows the seats at seed time; earlier runs may have used some. A full licence is skipped.
                AgentIdentity? identity = null;
                var key = slots[(n - start - 1) % slots.Count];
                for (var attempt = 0; identity is null && attempt < 20; attempt++)
                {
                    try
                    {
                        identity = await cloud.EnrollAsync(new EnrollRequest(key, fingerprint, $"LOAD-{n:0000}"), attempts: 30);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("LIC_ACTIVATION_LIMIT_REACHED", StringComparison.Ordinal))
                    {
                        lock (lockObject)
                        {
                            exhausted.Add(key);
                            var remaining = free.Select(l => l.Key).Where(k => !exhausted.Contains(k)).ToList();
                            if (remaining.Count == 0)
                                throw;
                            key = remaining[Random.Shared.Next(remaining.Count)];
                        }
                    }
                }

                if (identity is null)
                    throw new InvalidOperationException($"No free seat for {fingerprint}.");
                lock (lockObject)
                {
                    var saved = new StoredDevice("load", identity.DeviceId, identity.DeviceSecret, identity.Fingerprint, identity.Hostname, identity.GatewayUrl);
                    store.Devices.Add(saved);
                    mine.Add(saved);
                    if (++next % 100 == 0)
                    {
                        store.Save(options.StorePath);
                        Console.WriteLine($"  {next} enrolled");
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
        store.Save(options.StorePath);
        return mine;
    }
}
