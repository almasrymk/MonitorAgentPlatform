using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Licensing.Http;
using Polly;

namespace MonitorCloud.Infrastructure.Licensing;

internal static class LicensingRegistration
{
    public static IServiceCollection AddLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LicensingSettings>()
            .Bind(configuration.GetSection(LicensingSettings.Section))
            .Validate(s => !s.IsLive || (Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _) && s.ClientId.Length > 0 && s.ClientSecret.Length > 0),
                "Licensing:BaseUrl, ClientId and ClientSecret are required in Live mode.")
            .ValidateOnStart();

        var settings = configuration.GetSection(LicensingSettings.Section).Get<LicensingSettings>() ?? new LicensingSettings();
        services.AddSingleton<FakeLicensingStore>();
        if (settings.IsLive)
        {
            var baseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            services.AddHttpClient(LicensingTokenCache.ClientName, c => c.BaseAddress = baseAddress);
            services.AddSingleton<LicensingTokenCache>();
            services.AddHttpClient<ILicensingGateway, HttpLicensingGateway>(c => c.BaseAddress = baseAddress)
                .AddResilienceHandler("licensing", builder =>
                {
                    // 3 retries with jitter on 5xx and timeouts, never on 4xx; 10 s per attempt; circuit breaker.
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        UseJitter = true,
                        BackoffType = DelayBackoffType.Exponential,
                        ShouldHandle = args => ValueTask.FromResult(
                            args.Outcome.Exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException
                            || (args.Outcome.Result is { } r && (int)r.StatusCode >= 500)),
                    });
                    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions());
                    builder.AddTimeout(TimeSpan.FromSeconds(10));
                });
        }
        else
        {
            services.AddScoped<ILicensingGateway, FakeLicensingGateway>();
        }

        services.AddHostedService<LicensingSyncJob>();
        return services;
    }

    /// <summary>Loads <c>Licensing:FakeDataPath</c> into the fake store at start-up (Development).</summary>
    public static void LoadFakeData(IServiceProvider services, string contentRoot)
    {
        var settings = services.GetRequiredService<IOptions<LicensingSettings>>().Value;
        if (settings.IsLive || string.IsNullOrWhiteSpace(settings.FakeDataPath))
            return;
        services.GetRequiredService<FakeLicensingStore>().LoadFile(Path.GetFullPath(Path.Combine(contentRoot, settings.FakeDataPath)));
    }
}
