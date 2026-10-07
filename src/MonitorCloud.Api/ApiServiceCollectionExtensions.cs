using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MonitorCloud.Api.Authentication;
using MonitorCloud.Api.Infrastructure;
using MonitorCloud.Application;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.AgentGateway;
using MonitorCloud.Api.Realtime;
using MonitorCloud.Infrastructure;
using Serilog;

namespace MonitorCloud.Api;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "portal";
    public const string AuthRateLimit = "auth";
    public const string EnrollRateLimit = "agent-enroll";
    public const string AgentTokenRateLimit = "agent-token";
    public const string LiveHubPath = "/hubs/live";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddMonitorAuthentication();
        services.AddAgentGateway(configuration);
        services.AddSignalR(o => o.AddFilter<HubCallerFilter>());
        services.AddSingleton<Application.Abstractions.Realtime.ILiveNotifier, LiveNotifier>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnricher.Enrich);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            })
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ModelStateProblem.Create);
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        services.AddOpenApi("v1");
        services.AddResponseCompression(o => o.EnableForHttps = true);

        // Per-IP limit on auth/* (01 section 7); tuned in M10.
        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 20);
        // 05 section 1.1: 10 enrollments per minute per IP (5 per hour per fingerprint is checked by the handler).
        var enrollLimit = configuration.GetValue("RateLimiting:Enroll:PermitLimit", 10);
        // Token exchange: per IP here; 5 failures per device id block that id (DeviceCredential).
        var tokenLimit = configuration.GetValue("RateLimiting:AgentToken:PermitLimit", 120);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(EnrollRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = enrollLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(AgentTokenRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = tokenLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = { Status = 429, Title = "Too Many Requests", Detail = "Too many requests. Try again later." },
                });
            };
        });

        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "ETag", "Retry-After")));

        if (!environment.IsDevelopment())
            services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseResponseCompression();
        app.UseCors(CorsPolicy);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<TenantContextMiddleware>();
        app.UseAuthorization();

        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();

        app.MapControllers();
        app.MapHub<LiveHub>(LiveHubPath);
        app.MapAgentGateway();
        return app;
    }
}
