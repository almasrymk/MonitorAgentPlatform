using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using MonitorCloud.Api.Infrastructure;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.Api;

public sealed class HttpPrimitivesTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();

    private static HttpCurrentUser UserFrom(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length == 0 ? null : "Bearer")),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.7");
        context.Items[CorrelationIdMiddleware.ItemKey] = "corr-1";
        return new HttpCurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Tenant_user_claims_are_read()
    {
        var location = Guid.CreateVersion7();
        var user = UserFrom(
            new Claim("sub", UserId.ToString()),
            new Claim("typ", "user"),
            new Claim("tid", TenantId.ToString()),
            new Claim("role", "Technician"),
            new Claim("name", "Sample Technician"),
            new Claim("loc", $"{location}, not-a-guid"),
            new Claim("perm", "devices.read"));

        user.IsAuthenticated.ShouldBeTrue();
        user.ActorType.ShouldBe(ActorType.User);
        user.UserId.ShouldBe(UserId);
        user.DeviceId.ShouldBeNull();
        user.TenantId.ShouldBe(TenantId);
        user.Role.ShouldBe("Technician");
        user.Name.ShouldBe("Sample Technician");
        user.IsPlatform.ShouldBeFalse();
        user.LocationScope.ShouldBe([location]);
        user.HasPermission("devices.read").ShouldBeTrue();
        user.HasPermission("devices.manage").ShouldBeFalse();
        user.IpAddress.ShouldBe("192.0.2.7");
        user.CorrelationId.ShouldBe("corr-1");
    }

    [Fact]
    public void Platform_user_has_a_role_and_no_tenant()
    {
        var user = UserFrom(new Claim("sub", UserId.ToString()), new Claim("typ", "user"), new Claim("role", "PlatformAdmin"));

        user.IsPlatform.ShouldBeTrue();
        user.TenantId.ShouldBeNull();
        user.LocationScope.ShouldBeEmpty();
    }

    [Fact]
    public void Device_token_is_a_device_actor()
    {
        var device = Guid.CreateVersion7();
        var user = UserFrom(new Claim("sub", device.ToString()), new Claim("typ", "device"), new Claim("tid", TenantId.ToString()));

        user.ActorType.ShouldBe(ActorType.Device);
        user.DeviceId.ShouldBe(device);
        user.UserId.ShouldBeNull();
        user.IsPlatform.ShouldBeFalse();
    }

    [Fact]
    public void Anonymous_request_is_not_authenticated()
    {
        var user = UserFrom();

        user.IsAuthenticated.ShouldBeFalse();
        user.ActorType.ShouldBe(ActorType.User);
        user.IsPlatform.ShouldBeFalse();
    }

    [Fact]
    public void Outside_a_request_the_actor_is_the_system()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        user.ActorType.ShouldBe(ActorType.System);
        user.IsAuthenticated.ShouldBeFalse();
        user.IpAddress.ShouldBeNull();
        user.CorrelationId.ShouldBeNull();
    }

    [Fact]
    public void Domain_errors_become_problem_details_with_their_code()
    {
        var problem = ProblemDetailsEnricher.FromError(Error.Locked("AUTH_LOCKED", "Locked for 15 minutes."), new DefaultHttpContext());

        problem.Status.ShouldBe(423);
        problem.Detail.ShouldBe("Locked for 15 minutes.");
        problem.Extensions["code"].ShouldBe("AUTH_LOCKED");
    }

    [Fact]
    public void Validation_errors_keep_their_field_errors()
    {
        var error = Error.Validation("VALIDATION_FAILED", "Invalid.") with
        {
            FieldErrors = new Dictionary<string, string[]> { ["email"] = ["Required."] },
        };

        var problem = ProblemDetailsEnricher.FromError(error, new DefaultHttpContext());

        problem.ShouldBeOfType<ValidationProblemDetails>().Errors["email"].ShouldBe(["Required."]);
    }

    [Fact]
    public void Enrich_adds_a_default_code_and_the_trace_id()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Path = "/api/v1/x";
        context.Response.StatusCode = 404;
        var problem = new ProblemDetails();

        ProblemDetailsEnricher.Enrich(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });

        problem.Status.ShouldBe(404);
        problem.Extensions["code"].ShouldBe("NOT_FOUND");
        problem.Extensions["traceId"].ShouldNotBeNull();
        problem.Instance.ShouldBe("/api/v1/x");
    }

    [Fact]
    public void Model_binding_errors_use_VALIDATION_FAILED()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Email", "The Email field is required.");
        modelState.AddModelError(string.Empty, string.Empty);
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);

        var result = ModelStateProblem.Create(action).ShouldBeOfType<ObjectResult>();

        result.StatusCode.ShouldBe(400);
        var problem = result.Value.ShouldBeOfType<ValidationProblemDetails>();
        problem.Extensions["code"].ShouldBe("VALIDATION_FAILED");
        problem.Errors["email"].ShouldBe(["The Email field is required."]);
        problem.Errors["body"].ShouldBe(["The value is invalid."]);
    }
}
