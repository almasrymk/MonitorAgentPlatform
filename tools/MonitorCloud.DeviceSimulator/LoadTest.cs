using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.SimulatedAgent;

/// <summary>
/// <c>simulator load --devices 1500 --duration 10m --report docs/perf/load.json</c> (09 section 8): enrolls the devices
/// over the demo customers with free seats, keeps all streams open with heartbeats, one metric batch per device and
/// minute (spread over the minute) and a snapshot, and measures acknowledgement latency, data loss, the API latency
/// targets, the live update and offline detection seen by a SignalR client, and the server's memory.
/// </summary>
internal static class LoadTest
{
    /// <summary>Devices watched through the live hub (connect and drop latency).</summary>
    private const int Watched = 50;

    /// <summary>Watched devices dropped without a Goodbye at the end (offline detection).</summary>
    private const int Dropped = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(HttpClient http, AgentCloudClient cloud, Store store, Options options, int devices, TimeSpan duration, string reportPath, string? serverProcess)
    {
        var started = DateTimeOffset.UtcNow;
        Console.WriteLine($"Load test: {devices} devices for {duration}.");
        var enrolled = await EnrollAsync(cloud, store, options, devices);
        Console.WriteLine($"{enrolled.Count} devices enrolled in {(DateTimeOffset.UtcNow - started).TotalSeconds:0} s.");

        var watched = enrolled.Take(Watched).Select(d => d.DeviceId).ToHashSet();
        await using var live = await LiveWatcher.StartAsync(http, options, watched);

        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan, KeepAlivePingDelay = TimeSpan.FromSeconds(60) };
        using var channel = GrpcChannel.ForAddress(options.Gateway ?? enrolled[0].GatewayUrl, new GrpcChannelOptions { HttpHandler = handler });
        using var stop = new CancellationTokenSource(duration);
        var agents = new ConcurrentDictionary<Guid, SimulatedAgent>();
        var connectedAt = new ConcurrentDictionary<Guid, DateTimeOffset>();
        var batchesSent = 0;
        var connectFailures = 0;

        var runs = enrolled.Select(async (device, index) =>
        {
            // Spread the connections and the metric batches over the first minute.
            var offset = TimeSpan.FromMilliseconds(60_000.0 * index / enrolled.Count);
            await Task.Delay(offset);
            var identity = new AgentIdentity(device.DeviceId, device.DeviceSecret, device.Fingerprint, device.Hostname, device.GatewayUrl);
            var agent = new SimulatedAgent(identity, channel);
            agents[device.DeviceId] = agent;
            try
            {
                var token = await cloud.TokenAsync(identity, stop.Token);
                var sentHello = DateTimeOffset.UtcNow;
                if (await agent.ConnectAsync(token, cancellationToken: stop.Token) is null)
                {
                    Interlocked.Increment(ref connectFailures);
                    return;
                }

                connectedAt[device.DeviceId] = sentHello;
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

        var api = ApiProbe.RunAsync(http, options, stop.Token);
        var memory = SampleMemoryAsync(serverProcess, stop.Token);
        await Task.WhenAll(runs);

        // Give the last batches time to be acknowledged before counting losses.
        await Task.Delay(TimeSpan.FromSeconds(10));
        var latencies = agents.Values.SelectMany(a => a.AckLatenciesMs).Order().ToList();
        var unacknowledged = agents.Values.Sum(a => a.Unacknowledged);
        var apiSeries = await api;
        var memorySamples = await memory;

        // Live update: from the agent's Hello to deviceStateChanged (Online) at the portal's hub.
        var onlineMs = connectedAt.Where(c => watched.Contains(c.Key))
            .Select(c => live.FirstEvent(c.Key, "Online", c.Value) is { } at ? (at - c.Value).TotalMilliseconds : double.NaN)
            .Where(ms => !double.IsNaN(ms)).ToList();

        // Offline detection: watched devices close their stream without a Goodbye.
        var dropped = connectedAt.Keys.Where(watched.Contains).Take(Dropped).ToList();
        var droppedAt = DateTimeOffset.UtcNow;
        foreach (var id in dropped)
            await agents[id].DisposeAsync();
        var deadline = droppedAt.AddMinutes(3);
        while (DateTimeOffset.UtcNow < deadline && dropped.Any(id => live.FirstEvent(id, "Offline", droppedAt) is null))
            await Task.Delay(TimeSpan.FromSeconds(1));
        var offlineMs = dropped.Select(id => live.FirstEvent(id, "Offline", droppedAt) is { } at ? (at - droppedAt).TotalMilliseconds : double.NaN).ToList();

        foreach (var (id, agent) in agents)
        {
            if (dropped.Contains(id))
                continue;
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

        var apiReport = new JsonObject();
        foreach (var (name, values) in apiSeries)
            apiReport[name] = Percentiles(values);
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
            ["apiMs"] = apiReport,
            ["liveOnlineMs"] = Percentiles(onlineMs),
            ["offlineDetectionMs"] = Percentiles([.. offlineMs.Where(ms => !double.IsNaN(ms))]),
            ["offlineNotDetected"] = offlineMs.Count(double.IsNaN),
            ["liveHubConnected"] = live.Connected,
            ["serverWorkingSetMb"] = new JsonArray([.. memorySamples.Select(m => JsonValue.Create(m))]),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToJsonString(Json));
        Console.WriteLine(report.ToJsonString(Json));
        return 0;
    }

    internal static JsonObject Percentiles(List<double> values)
    {
        double P(double p) => values.Count == 0 ? 0 : Math.Round(values[Math.Clamp((int)Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1)], 1);
        values.Sort();
        return new JsonObject { ["count"] = values.Count, ["p50"] = P(0.5), ["p95"] = P(0.95), ["p99"] = P(0.99), ["max"] = values.Count == 0 ? 0 : Math.Round(values[^1], 1) };
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

/// <summary>
/// The API latency targets of 09 section 8, sampled while the load runs: device lists (plain and filtered), the three
/// dashboards (not cached), 7 days of device metrics and sign-in.
/// </summary>
internal static class ApiProbe
{
    private static readonly string[] Filters = ["status=critical", "os=linux", "license=unlicensed", "search=SRV", "status=offline&sort=lastSeen", "sort=cpu"];

    public static async Task<Dictionary<string, List<double>>> RunAsync(HttpClient http, Options options, CancellationToken cancellationToken)
    {
        var series = new Dictionary<string, List<double>>(StringComparer.Ordinal)
        {
            ["deviceList"] = [], ["deviceListFiltered"] = [], ["customerDashboard"] = [], ["locationDashboard"] = [], ["platformDashboard"] = [],
            ["deviceMetrics7d"] = [], ["signIn"] = [],
        };
        var tenant = await SignInAsync(http, $"admin@{options.Tenant}.test", options.AdminPassword, cancellationToken);
        var platform = await SignInAsync(http, "admin@monitor.local", options.PlatformPassword, cancellationToken);
        if (tenant is null || platform is null)
        {
            Console.WriteLine("API probe: sign-in failed, no API latencies measured.");
            return series;
        }

        var location = JsonNode.Parse(await GetAsync(http, tenant, "api/v1/locations?pageSize=1", cancellationToken))!["items"]![0]!["id"]!.GetValue<string>();
        var device = JsonNode.Parse(await GetAsync(http, tenant, "api/v1/devices?search=WEB-SRV-01&pageSize=1", cancellationToken))!["items"]![0]!["id"]!.GetValue<string>();
        var issued = DateTimeOffset.UtcNow;
        var round = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - issued > TimeSpan.FromMinutes(12))
                {
                    tenant = await SignInAsync(http, $"admin@{options.Tenant}.test", options.AdminPassword, cancellationToken) ?? tenant;
                    platform = await SignInAsync(http, "admin@monitor.local", options.PlatformPassword, cancellationToken) ?? platform;
                    issued = DateTimeOffset.UtcNow;
                }

                var to = DateTimeOffset.UtcNow;
                series["deviceList"].Add(await TimeAsync(http, tenant, "api/v1/devices?pageSize=24", cancellationToken));
                series["deviceListFiltered"].Add(await TimeAsync(http, tenant, $"api/v1/devices?pageSize=24&{Filters[round % Filters.Length]}", cancellationToken));
                series["customerDashboard"].Add(await TimeAsync(http, tenant, "api/v1/dashboard?trendDays=7", cancellationToken));
                series["locationDashboard"].Add(await TimeAsync(http, tenant, $"api/v1/locations/{location}/dashboard?trendDays=7", cancellationToken));
                series["platformDashboard"].Add(await TimeAsync(http, platform, "api/v1/platform/dashboard?trendDays=7", cancellationToken));
                series["deviceMetrics7d"].Add(await TimeAsync(http, tenant,
                    $"api/v1/devices/{device}/metrics?from={Uri.EscapeDataString(to.AddDays(-7).ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&metrics=cpu,ram,disk,network", cancellationToken));
                if (round % 5 == 0)
                {
                    var watch = Stopwatch.StartNew();
                    if (await SignInAsync(http, $"admin@{options.Tenant}.test", options.AdminPassword, cancellationToken) is not null)
                        series["signIn"].Add(watch.Elapsed.TotalMilliseconds);
                }

                round++;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return series;
    }

    private static async Task<string?> SignInAsync(HttpClient http, string email, string password, CancellationToken cancellationToken)
    {
        using var login = await http.PostAsJsonAsync(new Uri("api/v1/auth/login", UriKind.Relative), new { email, password }, cancellationToken);
        return login.IsSuccessStatusCode ? JsonNode.Parse(await login.Content.ReadAsStringAsync(cancellationToken))!["accessToken"]!.GetValue<string>() : null;
    }

    private static async Task<string> GetAsync(HttpClient http, string token, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
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
}

/// <summary>
/// The portal's view of the devices: a platform administrator connected to <c>/hubs/live</c>, subscribed to the
/// watched devices, records when each <c>deviceStateChanged</c> arrives.
/// </summary>
internal sealed class LiveWatcher : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<(string Connection, DateTimeOffset At)>> _events = new();
    private readonly HashSet<Guid> _watched;
    private HubConnection? _hub;

    private LiveWatcher(HashSet<Guid> watched) => _watched = watched;

    public bool Connected => _hub?.State == HubConnectionState.Connected;

    public static async Task<LiveWatcher> StartAsync(HttpClient http, Options options, HashSet<Guid> watched)
    {
        var watcher = new LiveWatcher(watched);
        using var login = await http.PostAsJsonAsync(new Uri("api/v1/auth/login", UriKind.Relative), new { email = "admin@monitor.local", password = options.PlatformPassword });
        if (!login.IsSuccessStatusCode)
        {
            Console.WriteLine("Live watcher: platform sign-in failed; live update and offline detection are not measured.");
            return watcher;
        }

        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
        watcher._hub = new HubConnectionBuilder()
            .WithUrl(new Uri(http.BaseAddress!, "hubs/live"), o => o.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .Build();
        watcher._hub.On<JsonElement>("deviceStateChanged", payload =>
        {
            var id = payload.GetProperty("deviceId").GetGuid();
            if (watched.Contains(id))
                watcher._events.GetOrAdd(id, _ => new()).Enqueue((payload.GetProperty("connection").GetString() ?? string.Empty, DateTimeOffset.UtcNow));
        });
        await watcher._hub.StartAsync();
        foreach (var id in watched)
            await watcher._hub.InvokeAsync("SubscribeDevice", id);
        return watcher;
    }

    /// <summary>The first event with <paramref name="connection"/> at or after <paramref name="since"/>.</summary>
    public DateTimeOffset? FirstEvent(Guid deviceId, string connection, DateTimeOffset since) =>
        _events.TryGetValue(deviceId, out var queue)
            ? queue.Where(e => e.Connection == connection && e.At >= since).Select(e => (DateTimeOffset?)e.At).FirstOrDefault()
            : null;

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
            await _hub.DisposeAsync();
    }
}
