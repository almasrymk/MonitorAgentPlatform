// Seeds a local Licensing Platform with the Monitor Agent product, the four plans, the detailed demo customers and
// the contract-test scenarios, creates an API client with the scopes Monitor Cloud needs, and writes
// seed/licensing-live.local.json (git-ignored) - 04 section 6. Idempotent: records are looked up by code or name.
//
// Usage:
//   dotnet run --project tools/MonitorCloud.LicensingSeeder -- --url http://localhost:5000 --email admin@vendor.test --password ...
//     [--tenant-id <guid>]   (platform admins: the vendor tenant, sent as X-Tenant-Id)
//     [--out seed/licensing-live.local.json]

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

var options = Args.Parse(args);
if (options.Url is null || options.Email is null || options.Password is null)
{
    Console.Error.WriteLine("Usage: --url <licensing base url> --email <tenant admin e-mail> --password <password> [--tenant-id <guid>] [--out <file>]");
    return 2;
}

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var http = new HttpClient { BaseAddress = new Uri(options.Url.TrimEnd('/') + "/") };
var login = await Api.Post(http, "api/v1/auth/login", new { email = options.Email, password = options.Password });
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login["accessToken"]!.GetValue<string>());
if (options.TenantId is not null)
    http.DefaultRequestHeaders.Add("X-Tenant-Id", options.TenantId);

var previous = File.Exists(options.Out) ? JsonNode.Parse(await File.ReadAllTextAsync(options.Out)) : null;

// Product and plans (08 section 2).
var product = await Api.FindOrCreate(http, "api/v1/products?pageSize=200", p => p["code"]?.GetValue<string>() == "000001",
    "api/v1/products", new { code = "000001", name = "Monitor Agent", description = "Monitor Agent service and desktop" });
var productId = product["id"]!.GetValue<Guid>();

var planSpecs = new (string Code, string Name, int Limit, string[] Features, decimal Price)[]
{
    ("STARTER", "Starter", 50, ["monitoring", "alerts", "reports.basic"], 49),
    ("PROFESSIONAL", "Professional", 200, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints"], 149),
    ("BUSINESS", "Business", 250, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints", "reports.advanced", "archive"], 249),
    ("ENTERPRISE", "Enterprise", 500, ["monitoring", "alerts", "reports.basic", "notifications.email", "monitorpoints", "reports.advanced", "archive", "notifications.webhook", "remote.actions"], 499),
    ("CONTRACT", "Contract Test (2 seats)", 2, ["monitoring", "alerts"], 0),
};
var plans = new Dictionary<string, Guid>();
foreach (var spec in planSpecs)
{
    var plan = await Api.FindOrCreate(http, $"api/v1/plans?pageSize=200&productId={productId}", p => p["code"]?.GetValue<string>() == spec.Code,
        "api/v1/plans", new
        {
            productId, code = spec.Code, name = spec.Name, price = spec.Price, currency = "USD", durationDays = 365, maxActivations = spec.Limit,
            heartbeatIntervalHours = 24, offlineGraceDays = 7, features = spec.Features,
        });
    var id = plan["id"]!.GetValue<Guid>();
    if (plan["status"]?.ToString() != "Published")
        await Api.Post(http, $"api/v1/plans/{id}/publish", new { }, allowEmpty: true);
    plans[spec.Code] = id;
}

// Customers, subscriptions and licences.
var customerSpecs = new (string Name, string Plan, string Scenario)[]
{
    ("Acme Corporation", "ENTERPRISE", "active"), ("Nile Trading Group", "BUSINESS", ""), ("Delta Logistics", "PROFESSIONAL", ""),
    ("Horizon Retail", "ENTERPRISE", ""), ("Sahara Foods", "BUSINESS", ""), ("Gulf Engineering", "ENTERPRISE", ""),
    ("Pyramid Pharma", "PROFESSIONAL", ""), ("Oasis Hospitality", "STARTER", ""),
    ("Contract Limited Customer", "CONTRACT", "limited"), ("Contract Suspended Customer", "STARTER", "suspended"),
};
var written = new JsonArray();
var contract = new JsonObject();
foreach (var spec in customerSpecs)
{
    var customer = await Api.FindOrCreate(http, $"api/v1/customers?pageSize=200&search={Uri.EscapeDataString(spec.Name)}", c => c["name"]?.GetValue<string>() == spec.Name,
        "api/v1/customers", new { name = spec.Name, email = $"billing@{spec.Name.Split(' ')[0].ToLowerInvariant()}.test", country = "Egypt" }, unwrap: "customer");
    var customerId = customer["id"]!.GetValue<Guid>();

    var subscriptions = await Api.Get(http, $"api/v1/subscriptions?pageSize=200&customerId={customerId}");
    var subscription = subscriptions["items"]!.AsArray().FirstOrDefault(s => s?["customerId"]?.GetValue<Guid>() == customerId)
        ?? (await Api.Post(http, "api/v1/subscriptions", new { customerId, planId = plans[spec.Plan] }))["subscription"]!;
    var subscriptionId = subscription["id"]!.GetValue<Guid>();

    var licenses = await Api.Get(http, $"api/v1/licenses?pageSize=200&subscriptionId={subscriptionId}");
    var license = licenses["items"]!.AsArray().FirstOrDefault(l => l?["subscriptionId"]?.GetValue<Guid>() == subscriptionId);
    string? key = previous?["customers"]?.AsArray().FirstOrDefault(c => c?["name"]?.GetValue<string>() == spec.Name)?["productKey"]?.GetValue<string>();
    Guid licenseId;
    if (license is null)
    {
        var issued = await Api.Post(http, "api/v1/licenses", new { subscriptionId });
        licenseId = issued["license"]!["id"]!.GetValue<Guid>();
        key = issued["productKey"]!.GetValue<string>();
    }
    else
    {
        licenseId = license["id"]!.GetValue<Guid>();
        // The key is shown only once: regenerate it when this tool does not know it any more.
        key ??= (await Api.Post(http, $"api/v1/licenses/{licenseId}/regenerate-key", new { }))["productKey"]!.GetValue<string>();
    }

    if (spec.Scenario == "suspended" && license?["status"]?.ToString() != "Suspended")
        await Api.Post(http, $"api/v1/licenses/{licenseId}/suspend", new { reason = "Contract test scenario" }, allowEmpty: true);

    written.Add(new JsonObject { ["name"] = spec.Name, ["plan"] = spec.Plan, ["customerId"] = customerId, ["licenseId"] = licenseId, ["productKey"] = key });
    switch (spec.Scenario)
    {
        case "active":
            contract["PRODUCT_KEY"] = key;
            contract["CUSTOMER_ID"] = customerId.ToString();
            break;
        case "limited":
            contract["LIMITED_KEY"] = key;
            break;
        case "suspended":
            contract["SUSPENDED_KEY"] = key;
            break;
    }
}

// API client for Monitor Cloud (04 section 2 scopes); the secret is kept from the previous run when present.
var scopes = new[] { "licenses.activate", "licenses.validate", "licenses.read", "customers.read", "subscriptions.read", "catalog.read" };
var clientId = previous?["clientId"]?.GetValue<string>();
var clientSecret = previous?["clientSecret"]?.GetValue<string>();
if (clientId is null || clientSecret is null)
{
    var created = await Api.Post(http, "api/v1/api-clients", new { name = "Monitor Cloud", scopes });
    clientId = created["client"]!["clientId"]!.GetValue<string>();
    clientSecret = created["clientSecret"]!.GetValue<string>();
}

contract["URL"] = options.Url;
contract["CLIENT_ID"] = clientId;
contract["CLIENT_SECRET"] = clientSecret;
contract["PRODUCT_CODE"] = "000001";
var output = new JsonObject
{
    ["baseUrl"] = options.Url,
    ["clientId"] = clientId,
    ["clientSecret"] = clientSecret,
    ["productCode"] = "000001",
    ["customers"] = written,
    ["contract"] = contract,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Out))!);
await File.WriteAllTextAsync(options.Out, output.ToJsonString(json));

Console.WriteLine($"Wrote {options.Out}. Contract test environment:");
foreach (var (name, value) in contract)
    Console.WriteLine($"  LICENSING_CONTRACT_{name}={value}");
return 0;

internal sealed record Args(string? Url, string? Email, string? Password, string? TenantId, string Out)
{
    public static Args Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        return new Args(Value("--url"), Value("--email"), Value("--password"), Value("--tenant-id"), Value("--out") ?? Path.Combine("seed", "licensing-live.local.json"));
    }
}

internal static class Api
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<JsonNode> Get(HttpClient http, string url)
    {
        using var response = await http.GetAsync(new Uri(url, UriKind.Relative));
        return await Read(response, url);
    }

    public static async Task<JsonNode> Post(HttpClient http, string url, object body, bool allowEmpty = false)
    {
        using var response = await http.PostAsJsonAsync(new Uri(url, UriKind.Relative), body, Json);
        if (allowEmpty && response.IsSuccessStatusCode && (response.Content.Headers.ContentLength ?? 0) == 0)
            return new JsonObject();
        return await Read(response, url);
    }

    public static async Task<JsonNode> FindOrCreate(HttpClient http, string listUrl, Func<JsonNode, bool> match, string createUrl, object body, string? unwrap = null)
    {
        var existing = (await Get(http, listUrl))["items"]!.AsArray().OfType<JsonNode>().FirstOrDefault(match);
        if (existing is not null)
            return existing;
        var created = await Post(http, createUrl, body);
        return unwrap is null ? created : created[unwrap]!;
    }

    private static async Task<JsonNode> Read(HttpResponseMessage response, string url)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} from {url}: {text}");
        return JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text)!;
    }
}
