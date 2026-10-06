using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Infrastructure.Options;

namespace MonitorCloud.Api.Authentication;

/// <summary>
/// Two JWT bearer schemes (03 section 6): <c>Bearer</c> for users and <c>Device</c> for agents. The REST API accepts
/// only <c>typ=user</c> tokens; the gateway (M4) only <c>typ=device</c>. Token lifetimes use <see cref="TimeProvider"/>.
/// </summary>
public static class AuthenticationSetup
{
    public const string UserScheme = JwtBearerDefaults.AuthenticationScheme;
    public const string DeviceScheme = "Device";

    public static IServiceCollection AddMonitorAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(UserScheme)
            .AddJwtBearer(UserScheme, _ => { })
            .AddJwtBearer(DeviceScheme, _ => { });
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureJwtBearer>();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(UserScheme).RequireAuthenticatedUser().RequireClaim("typ", "user").Build())
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(UserScheme).RequireAuthenticatedUser().RequireClaim("typ", "user").Build())
            .AddPolicy(DeviceScheme, p => p.AddAuthenticationSchemes(DeviceScheme).RequireAuthenticatedUser().RequireClaim("typ", "device"));
        return services;
    }

    private sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt, TimeProvider clock) : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            var settings = jwt.Value;
            var device = name == DeviceScheme;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = device ? settings.DeviceAudience : settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(device ? settings.DeviceSigningKey : settings.SigningKey)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore.Value <= now.Add(parameters.ClockSkew))
                        && expires is not null && expires.Value >= now.Subtract(parameters.ClockSkew);
                },
                NameClaimType = "name",
                RoleClaimType = "role",
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var typ = context.Principal?.FindFirst("typ")?.Value;
                    if (typ != (device ? "device" : "user"))
                        context.Fail("Wrong token type.");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    // Problem details with AUTH_UNAUTHORIZED instead of an empty 401.
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problems.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = { Status = 401, Title = "Unauthorized", Detail = "Authentication is required." },
                    });
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problems.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = { Status = 403, Title = "Forbidden", Detail = "You do not have permission to perform this action." },
                    });
                },
            };
        }
    }
}
