using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Licensing.Http;

/// <summary>
/// The <c>Live</c> adapter: typed HttpClient against the Licensing Platform with a cached client-credentials token
/// (renewed one minute before expiry, retried once on 401). Resilience (timeout, retries on 5xx/timeouts only,
/// circuit breaker) is configured on the HttpClient. Problem <c>code</c>s are passed through unchanged.
/// </summary>
public sealed class HttpLicensingGateway(HttpClient http, LicensingTokenCache tokens, IOptions<LicensingSettings> options) : ILicensingGateway
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Product => Uri.EscapeDataString(options.Value.ProductCode);

    private sealed record ActivationBody(
        string Status, string LicenseNumber, string ProductCode, string PlanCode, List<string> Features, DateTimeOffset? ExpiresAt, int? MaxActivations,
        int ActiveActivations, DateTimeOffset CheckAfter, DateTimeOffset OfflineValidUntil, string Token, string Kid, bool AlreadyActivated,
        Guid LicenseId, Guid CustomerId, Guid SubscriptionId);

    public async Task<Result<SeatActivation>> ActivateSeatAsync(ActivateSeatRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new { productKey = request.ProductKey, deviceId = request.DeviceId, deviceName = request.DeviceName, productCode = options.Value.ProductCode, appVersion = request.AppVersion, os = request.Os };
        var result = await SendAsync<ActivationBody>(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/licensing/activate") { Content = JsonContent.Create(body, options: Json) };
            message.Headers.Add("Idempotency-Key", request.IdempotencyKey);
            return message;
        }, ct);
        return result.IsSuccess ? Map(result.Value) : result.Error!;
    }

    public async Task<Result<SeatActivation>> RefreshSeatAsync(Guid licenseId, string deviceId, string? appVersion, string? os, CancellationToken ct)
    {
        var result = await SendAsync<ActivationBody>(() => new HttpRequestMessage(HttpMethod.Post, $"api/v1/integration/licenses/{licenseId}/devices/{Uri.EscapeDataString(deviceId)}/heartbeat")
        {
            Content = JsonContent.Create(new { appVersion, os }, options: Json),
        }, ct);
        return result.IsSuccess ? Map(result.Value) : result.Error!;
    }

    public async Task<Result> ReleaseSeatAsync(Guid licenseId, string deviceId, CancellationToken ct)
    {
        var result = await SendAsync<object>(() => new HttpRequestMessage(HttpMethod.Post, $"api/v1/integration/licenses/{licenseId}/devices/{Uri.EscapeDataString(deviceId)}/release"), ct, expectBody: false);
        return result.IsSuccess ? Result.Success() : result.Error!;
    }

    public Task<Result<CustomerEntitlements>> GetEntitlementsAsync(Guid licensingCustomerId, CancellationToken ct) =>
        SendAsync<CustomerEntitlements>(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/integration/customers/{licensingCustomerId}/entitlements?productCode={Product}"), ct);

    public Task<Result<PagedResult<LicensingCustomer>>> ListCustomersAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var since = updatedSince is { } s ? $"&updatedSince={Uri.EscapeDataString(s.ToString("O"))}" : string.Empty;
        return SendAsync<PagedResult<LicensingCustomer>>(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/integration/customers?page={page}&pageSize={pageSize}{since}"), ct);
    }

    public async Task<Result<IReadOnlyList<LicensingPlan>>> ListPlansAsync(CancellationToken ct)
    {
        var result = await SendAsync<List<LicensingPlan>>(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/integration/plans?productCode={Product}"), ct);
        return result.IsSuccess ? result.Value : result.Error!;
    }

    public Task<Result<ChangeFeedPage>> GetChangesAsync(string? cursor, int take, CancellationToken ct)
    {
        var query = cursor is null ? $"take={take}" : $"take={take}&cursor={Uri.EscapeDataString(cursor)}";
        return SendAsync<ChangeFeedPage>(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/integration/changes?{query}"), ct);
    }

    public async Task<Result<string>> GetSigningKeysJsonAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(new Uri("api/v1/signing-keys", UriKind.Relative), ct);
            if (!response.IsSuccessStatusCode)
                return await ProblemAsync(response, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LicensingErrors.Unavailable;
        }
    }

    private static SeatActivation Map(ActivationBody b) =>
        new(b.LicenseId, b.CustomerId, b.SubscriptionId, b.LicenseNumber, b.PlanCode, b.Features, b.ExpiresAt, b.MaxActivations, b.ActiveActivations,
            b.CheckAfter, b.OfflineValidUntil, b.Token, b.Kid, b.AlreadyActivated);

    /// <summary>Sends with the client token; on 401 the token is renewed and the request retried once.</summary>
    private async Task<Result<T>> SendAsync<T>(Func<HttpRequestMessage> create, CancellationToken ct, bool expectBody = true)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await tokens.GetAsync(forceRefresh: attempt > 0, ct);
                if (token.IsFailure)
                    return token.Error!;

                using var request = create();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                    continue;
                if (!response.IsSuccessStatusCode)
                    return await ProblemAsync(response, ct);
                if (!expectBody || response.StatusCode == HttpStatusCode.NoContent)
                    return Result<T>.Ok(default!);
                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return value is null ? LicensingErrors.Unavailable : value;
            }

            return Error.Unauthorized("LICENSING_UNAUTHORIZED", "The Licensing Platform rejected the client credentials.");
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return LicensingErrors.Unavailable;
        }
    }

    internal static async Task<Error> ProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? code = null;
        string? title = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Not a problem body; the status decides.
        }

        if ((int)response.StatusCode == 429)
            code ??= "RATE_LIMITED";
        return LicensingProblem.FromStatus((int)response.StatusCode, code, title);
    }

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}

/// <summary>Client-credentials token of the Licensing API client, cached until one minute before it expires.</summary>
public sealed class LicensingTokenCache(IHttpClientFactory factory, IOptions<LicensingSettings> options, TimeProvider clock) : IDisposable
{
    public const string ClientName = "licensing-auth";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    private sealed record TokenBody(string AccessToken, string TokenType, int ExpiresIn, string Scope);

    public void Dispose() => _gate.Dispose();

    public async Task<Result<string>> GetAsync(bool forceRefresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null && clock.GetUtcNow() < _expiresAt.AddMinutes(-1))
                return _token;

            using var client = factory.CreateClient(ClientName);
            using var response = await client.PostAsJsonAsync(
                new Uri("api/v1/auth/client-token", UriKind.Relative),
                new { clientId = options.Value.ClientId, clientSecret = options.Value.ClientSecret },
                HttpLicensingGateway.Json,
                ct);
            if (!response.IsSuccessStatusCode)
                return await HttpLicensingGateway.ProblemAsync(response, ct);
            var body = await response.Content.ReadFromJsonAsync<TokenBody>(HttpLicensingGateway.Json, ct);
            if (body is null)
                return LicensingErrors.Unavailable;
            _token = body.AccessToken;
            _expiresAt = clock.GetUtcNow().AddSeconds(body.ExpiresIn);
            return _token;
        }
        catch (HttpRequestException)
        {
            return LicensingErrors.Unavailable;
        }
        finally
        {
            _gate.Release();
        }
    }
}
