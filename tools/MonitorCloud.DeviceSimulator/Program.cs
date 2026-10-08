// Device simulator (05 section 11): enrolls simulated devices and keeps their gRPC streams open.
//
//   simulator enroll --count 20 [--tenant acme] [--location CAIRO-HQ]
//   simulator run --devices 20 [--tenant acme] [--location CAIRO-HQ] [--seconds 60]  (stop with Goodbye after 60 s)
//   simulator load --devices 1500 --duration 10m --report docs/perf/load.json [--server-process MonitorCloud.Api]
//   simulator run --devices 1 --issue cpu [--issue-after 5] [--clear-after 40]   (raises a Critical issue, clears it later)
//
// Options: --api http://localhost:5300, --store seed/simulator.local.json (git-ignored: it holds device secrets),
// --licensing seed/licensing-fake.json (demo product keys, Fake mode), --admin-email / --admin-password (to create
// the location enrollment code; default admin@{tenant}.test with the demo password of 08), --run-id <id> (new
// fingerprints for this run, e.g. for tests: re-enrolling one fingerprint is limited to 5 per hour).
// Metrics, scenarios and load runs arrive with telemetry ingestion (M5) and load tests (M10).

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Net.Client;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.SimulatedAgent;

var options = Options.Parse(args);
if (options.Command is not ("enroll" or "run" or "load"))
{
    Console.Error.WriteLine("Usage: simulator enroll --count <n> | run --devices <n>  [--tenant acme] [--location CAIRO-HQ] [--api <url>]");
    Console.Error.WriteLine("Scenarios and load runs (--scenario, load) arrive with M5 and M10.");
    return 2;
}

using var http = new HttpClient { BaseAddress = new Uri(options.Api.TrimEnd('/') + "/") };
var cloud = new AgentCloudClient(http);
var store = Store.Load(options.StorePath);
if (options.Command == "load")
    return await LoadTest.RunAsync(http, cloud, store, options, options.Devices, options.Duration, options.Report, options.ServerProcess);
var wanted = options.Command == "enroll" ? options.Count : options.Devices;
var mine = store.Devices.Where(d => d.Tenant == options.Tenant).ToList();

// Devices whose credential no longer works (retired, reinstalled elsewhere) enroll again: same fingerprint, so the
// cloud reactivates the same device in its previous location.
var stale = new List<StoredDevice>();
foreach (var device in mine.Take(wanted))
{
    try
    {
        await cloud.TokenAsync(new AgentIdentity(device.DeviceId, device.DeviceSecret, device.Fingerprint, device.Hostname, device.GatewayUrl));
    }
    catch (InvalidOperationException)
    {
        stale.Add(device);
    }
}

if (mine.Count < wanted || stale.Count > 0)
{
    var key = ProductKeys.For(options.LicensingPath, options.Tenant);
    var code = await LocationCodes.CreateAsync(http, options);
    foreach (var device in stale)
    {
        var identity = await cloud.EnrollAsync(new EnrollRequest(key, device.Fingerprint, device.Hostname, LocationCode: code));
        var renewed = device with { DeviceId = identity.DeviceId, DeviceSecret = identity.DeviceSecret, GatewayUrl = identity.GatewayUrl };
        store.Devices[store.Devices.IndexOf(device)] = renewed;
        mine[mine.IndexOf(device)] = renewed;
        store.Save(options.StorePath);
        Console.WriteLine($"Enrolled {identity.Hostname} again ({identity.DeviceId})");
    }

    for (var n = mine.Count + 1; n <= wanted; n++)
    {
        var fingerprint = options.RunId is null ? $"sim-{options.Tenant}-{n:0000}" : $"sim-{options.Tenant}-{options.RunId}-{n:0000}";
        var identity = await cloud.EnrollAsync(new EnrollRequest(key, fingerprint, $"SIM-{options.Tenant.ToUpperInvariant()}-{n:00}", LocationCode: code));
        var saved = new StoredDevice(options.Tenant, identity.DeviceId, identity.DeviceSecret, identity.Fingerprint, identity.Hostname, identity.GatewayUrl);
        store.Devices.Add(saved);
        mine.Add(saved);
        store.Save(options.StorePath);
        Console.WriteLine($"Enrolled {identity.Hostname} ({identity.DeviceId})");
    }
}
if (options.Command == "enroll")
    return 0;

using var stop = options.Seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

var gateway = options.Gateway ?? mine[0].GatewayUrl;
Console.WriteLine($"Running {wanted} devices against {gateway}. Ctrl+C sends Goodbye and stops.");
using var channel = GrpcChannel.ForAddress(gateway);
var runs = mine.Take(wanted).Select(d => RunDeviceAsync(d, channel, stop.Token)).ToList();
await Task.WhenAll(runs);
return 0;

async Task RunDeviceAsync(StoredDevice device, GrpcChannel grpc, CancellationToken cancellationToken)
{
    var identity = new AgentIdentity(device.DeviceId, device.DeviceSecret, device.Fingerprint, device.Hostname, device.GatewayUrl);
    var backoff = TimeSpan.FromSeconds(1);
    while (!cancellationToken.IsCancellationRequested)
    {
        await using var agent = new SimulatedAgent(identity, grpc);
        try
        {
            var token = await cloud.TokenAsync(identity, cancellationToken);
            var welcome = await agent.ConnectAsync(token, cancellationToken: cancellationToken);
            if (welcome is null)
            {
                Console.WriteLine($"{device.Hostname}: refused {agent.Disconnected?.Code ?? agent.Failure?.ToString() ?? "stream closed"}");
                if (agent.Disconnected is { RetryAfterSeconds: 0 })
                    return;
            }
            else
            {
                backoff = TimeSpan.FromSeconds(1);
                Console.WriteLine($"{device.Hostname}: online (session {welcome.SessionId})");
                await agent.InventoryAsync(InventoryKind.Hardware, cancellationToken);
                await agent.InventoryAsync(InventoryKind.Os, cancellationToken);
                using var telemetry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var pump = PumpTelemetryAsync(agent, telemetry.Token);
                var issue = options.Issue is null ? Task.CompletedTask : IssueScenarioAsync(agent, device.Hostname, telemetry.Token);
                await agent.RunHeartbeatsAsync(cancellationToken);
                await telemetry.CancelAsync();
                await pump;
                await issue;
                if (cancellationToken.IsCancellationRequested)
                {
                    await agent.GoodbyeAsync(GoodbyeReason.ServiceStopping, CancellationToken.None);
                    Console.WriteLine($"{device.Hostname}: goodbye");
                    return;
                }

                Console.WriteLine($"{device.Hostname}: disconnected {agent.Disconnected?.Code ?? "stream closed"}");
                if (agent.Disconnected is { RetryAfterSeconds: > 0 } d)
                    backoff = TimeSpan.FromSeconds(d.RetryAfterSeconds);
                else if (agent.Disconnected is not null)
                    return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or Grpc.Core.RpcException)
        {
            Console.WriteLine($"{device.Hostname}: {ex.Message}");
        }

        // Exponential back-off 1 s -> 5 min with +/-20% jitter (AG-10).
        try
        {
            await Task.Delay(backoff * (0.8 + (Random.Shared.NextDouble() * 0.4)), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        backoff = TimeSpan.FromSeconds(Math.Min(300, backoff.TotalSeconds * 2));
    }
}

// --issue: one Critical IssueEvent after --issue-after seconds, cleared after --clear-after seconds (M6 e2e flow).
async Task IssueScenarioAsync(SimulatedAgent agent, string hostname, CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(options.IssueAfter), cancellationToken);
        await agent.IssueAsync(options.Issue!, IssueAction.Raised, Severity.Critical, "Performance", $"Simulated {options.Issue} issue", cancellationToken: cancellationToken);
        Console.WriteLine($"{hostname}: raised {options.Issue}");
        if (options.ClearAfter <= 0)
            return;
        await Task.Delay(TimeSpan.FromSeconds(options.ClearAfter), cancellationToken);
        await agent.IssueAsync(options.Issue!, IssueAction.Cleared, cancellationToken: cancellationToken);
        Console.WriteLine($"{hostname}: cleared {options.Issue}");
    }
    catch (Exception ex) when (ex is OperationCanceledException or Grpc.Core.RpcException or InvalidOperationException)
    {
        // The run ended first.
    }
}

// One completed minute and a snapshot every minute (05 section 2, steady state); live samples follow SetTelemetryMode.
async Task PumpTelemetryAsync(SimulatedAgent agent, CancellationToken cancellationToken)
{
    try
    {
        await agent.SnapshotAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested && !agent.Completion.IsCompleted)
        {
            var now = DateTimeOffset.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(60 - now.Second), cancellationToken);
            await agent.SendMetricsAsync(DateTimeOffset.UtcNow.AddMinutes(-1), cancellationToken: cancellationToken);
            await agent.SnapshotAsync(cancellationToken);
        }
    }
    catch (Exception ex) when (ex is OperationCanceledException or Grpc.Core.RpcException or InvalidOperationException)
    {
        // The stream ended; the run loop reconnects.
    }
}

internal sealed record Options(string Command, int Count, int Devices, int Seconds, string Tenant, string Location, string Api, string? Gateway, string StorePath, string LicensingPath, string? AdminEmail, string AdminPassword, string? RunId, TimeSpan Duration, string Report, string? ServerProcess, string? Issue, int IssueAfter, int ClearAfter)
{
    public static Options Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        int Number(string name, int fallback) => int.TryParse(Value(name), out var n) && n > 0 ? n : fallback;
        var tenant = (Value("--tenant") ?? "acme").ToLowerInvariant();
        return new Options(
            args.Length > 0 ? args[0] : string.Empty, Number("--count", 1), Number("--devices", 1), Number("--seconds", 0), tenant, (Value("--location") ?? "CAIRO-HQ").ToUpperInvariant(),
            Value("--api") ?? "http://localhost:5300", Value("--gateway"), Value("--store") ?? Path.Combine("seed", "simulator.local.json"),
            Value("--licensing") ?? Path.Combine("seed", "licensing-fake.json"), Value("--admin-email"), Value("--admin-password") ?? "Demo@12345", Value("--run-id"),
            ParseDuration(Value("--duration")), Value("--report") ?? Path.Combine("docs", "perf", "load.json"), Value("--server-process"), Value("--issue"), Number("--issue-after", 5),
            int.TryParse(Value("--clear-after"), out var clear) ? clear : 0);
    }

    private static TimeSpan ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.FromMinutes(10);
        var number = double.Parse(value.TrimEnd('s', 'm', 'h'), System.Globalization.CultureInfo.InvariantCulture);
        return value.EndsWith('h') ? TimeSpan.FromHours(number) : value.EndsWith('s') ? TimeSpan.FromSeconds(number) : TimeSpan.FromMinutes(number);
    }
}

internal sealed record StoredDevice(string Tenant, Guid DeviceId, string DeviceSecret, string Fingerprint, string Hostname, string GatewayUrl);

internal sealed class Store
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public List<StoredDevice> Devices { get; set; } = [];

    public static Store Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Json) ?? new Store() : new Store();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}

internal static class ProductKeys
{
    /// <summary>The demo product key of a tenant code from the fake Licensing data (licence number LIC-{CODE}-0001).</summary>
    public static string For(string licensingPath, string tenant)
    {
        if (!File.Exists(licensingPath))
            throw new InvalidOperationException($"{licensingPath} not found: run the API once in Development to generate the demo seed.");
        var data = JsonNode.Parse(File.ReadAllText(licensingPath))!;
        var number = $"LIC-{tenant.ToUpperInvariant()}-0001";
        var license = data["licenses"]!.AsArray().FirstOrDefault(l => l?["licenseNumber"]?.GetValue<string>() == number)
            ?? throw new InvalidOperationException($"No demo licence {number} in {licensingPath}.");
        return license["productKey"]!.GetValue<string>();
    }
}

internal static class LocationCodes
{
    /// <summary>Signs in as the tenant administrator and creates a one-day enrollment code for the location.</summary>
    public static async Task<string?> CreateAsync(HttpClient http, Options options)
    {
        var email = options.AdminEmail ?? $"admin@{options.Tenant}.test";
        using var login = await http.PostAsJsonAsync(new Uri("api/v1/auth/login", UriKind.Relative), new { email, password = options.AdminPassword });
        if (!login.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sign-in as {email} failed ({(int)login.StatusCode}); pass --admin-email/--admin-password.");
        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/v1/locations?pageSize=200", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var list = await http.SendAsync(request);
        var location = JsonNode.Parse(await list.Content.ReadAsStringAsync())!["items"]!.AsArray()
            .FirstOrDefault(l => string.Equals(l?["code"]?.GetValue<string>(), options.Location, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Location {options.Location} not found for {options.Tenant}.");
        using var create = new HttpRequestMessage(HttpMethod.Post, new Uri($"api/v1/locations/{location["id"]}/enrollment-codes", UriKind.Relative))
        {
            Content = JsonContent.Create(new { expiresInHours = 24 }),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await http.SendAsync(create);
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"Creating an enrollment code failed ({(int)created.StatusCode}).");
        return JsonNode.Parse(await created.Content.ReadAsStringAsync())!["code"]!.GetValue<string>();
    }
}
