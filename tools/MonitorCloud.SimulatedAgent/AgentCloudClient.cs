using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorCloud.SimulatedAgent;

/// <summary>What an enrolled (simulated) agent keeps: its id and secret, and where the gateway is.</summary>
public sealed record AgentIdentity(Guid DeviceId, string DeviceSecret, string Fingerprint, string Hostname, string GatewayUrl);

public sealed record EnrollRequest(string ProductKey, string Fingerprint, string Hostname, string OsFamily = "Windows", string? LocationCode = null, int ProtocolVersion = 1);

/// <summary>The agent's HTTPS calls (05 section 1): enroll and token.</summary>
public sealed class AgentCloudClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Enrolls; a 429 (10 enrollments per minute per IP) is retried after <c>Retry-After</c>, up to <paramref name="attempts"/> times.</summary>
    public async Task<AgentIdentity> EnrollAsync(EnrollRequest request, int attempts = 5, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await EnrollOnceAsync(request, cancellationToken);
            }
            catch (RateLimitedException limited) when (attempt < attempts)
            {
                await Task.Delay(limited.RetryAfter, cancellationToken);
            }
        }
    }

    private async Task<AgentIdentity> EnrollOnceAsync(EnrollRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await http.PostAsJsonAsync(new Uri("/api/agent/v1/enroll", UriKind.Relative), new
        {
            productKey = request.ProductKey, fingerprint = request.Fingerprint, hostname = request.Hostname, osFamily = request.OsFamily,
            osName = OsName(request.OsFamily), osVersion = "1.0", architecture = "x64", agentVersion = SimulatedAgent.AgentVersion,
            protocolVersion = request.ProtocolVersion, locationCode = request.LocationCode,
        }, Json, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new RateLimitedException(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Enrollment of {request.Fingerprint} failed: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new AgentIdentity(root.GetProperty("deviceId").GetGuid(), root.GetProperty("deviceSecret").GetString()!, request.Fingerprint, request.Hostname,
            root.GetProperty("gatewayUrl").GetString() ?? string.Empty);
    }

    public async Task<string> TokenAsync(AgentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using var response = await http.PostAsJsonAsync(new Uri("/api/agent/v1/token", UriKind.Relative), new { deviceId = identity.DeviceId, deviceSecret = identity.DeviceSecret }, Json, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token for {identity.DeviceId} failed: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    internal static string OsName(string family) => family.ToLowerInvariant() switch
    {
        "linux" => "Ubuntu 22.04 LTS",
        "macos" => "macOS Sonoma 14.5",
        _ => "Windows Server 2022",
    };
}

/// <summary>The cloud answered 429.</summary>
public sealed class RateLimitedException(TimeSpan retryAfter) : Exception($"Rate limited; retry after {retryAfter.TotalSeconds:0} s.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}