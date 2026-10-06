using MediatR;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.UnitTests.Application;

public sealed class AuthorizationBehaviorTests
{
    private static readonly Guid TenantA = Guid.CreateVersion7();

    private static async Task<(TResponse Response, bool NextCalled)> Run<TRequest, TResponse>(
        TRequest request, TResponse success, ICurrentUser user, ITenantContext scope)
        where TRequest : notnull
        where TResponse : Result
    {
        var called = false;
        var behavior = new AuthorizationBehavior<TRequest, TResponse>(user, scope);
        var response = await behavior.Handle(request, _ =>
        {
            called = true;
            return Task.FromResult(success);
        }, CancellationToken.None);
        return (response, called);
    }

    private static Task<(Result<string> Response, bool NextCalled)> ReadDevices(ICurrentUser user, ITenantContext scope) =>
        Run(new ReadDevicesQuery("x"), Result<string>.Ok("ok"), user, scope);

    [Fact]
    public async Task Anonymous_request_passes_without_a_user()
    {
        var (response, called) = await Run(new SignInCommand("a@acme.test"), Result<string>.Ok("t"), TestCurrentUser.Anonymous(), TestTenantContext.Closed());

        response.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task Unauthenticated_caller_gets_AUTH_UNAUTHORIZED()
    {
        var (response, called) = await ReadDevices(TestCurrentUser.Anonymous(), TestTenantContext.Closed());

        response.Error!.Code.ShouldBe("AUTH_UNAUTHORIZED");
        response.Error.Kind.ShouldBe(ErrorKind.Unauthorized);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task Tenant_user_with_the_permission_passes()
    {
        var (response, called) = await ReadDevices(TestCurrentUser.Tenant(TenantA, "devices.read"), TestTenantContext.ForTenant(TenantA));

        response.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task Tenant_user_without_the_permission_gets_AUTH_FORBIDDEN()
    {
        var (response, called) = await ReadDevices(TestCurrentUser.Tenant(TenantA, "alerts.read"), TestTenantContext.ForTenant(TenantA));

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task Platform_user_without_a_workspace_gets_TENANT_SCOPE_REQUIRED_on_tenant_requests()
    {
        var (response, _) = await ReadDevices(TestCurrentUser.Platform("devices.read"), TestTenantContext.Platform());

        response.Error!.Code.ShouldBe("TENANT_SCOPE_REQUIRED");
    }

    [Fact]
    public async Task Platform_user_inside_a_workspace_passes_tenant_requests()
    {
        var (response, called) = await ReadDevices(TestCurrentUser.Platform("devices.read"), TestTenantContext.ForTenant(TenantA));

        response.IsSuccess.ShouldBeTrue();
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_request_passes_for_a_platform_user_in_platform_scope()
    {
        var (response, _) = await Run(new ListTenantsQuery(), Result<int>.Ok(1), TestCurrentUser.Platform("platform.tenants.read"), TestTenantContext.Platform());

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_request_is_forbidden_for_a_tenant_user_even_with_the_permission()
    {
        var (response, _) = await Run(new ListTenantsQuery(), Result<int>.Ok(1), TestCurrentUser.Tenant(TenantA, "platform.tenants.read"), TestTenantContext.ForTenant(TenantA));

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Platform_request_is_forbidden_inside_a_workspace()
    {
        var (response, _) = await Run(new ListTenantsQuery(), Result<int>.Ok(1), TestCurrentUser.Platform("platform.tenants.read"), TestTenantContext.ForTenant(TenantA));

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Platform_request_without_permission_attribute_passes_for_any_platform_user()
    {
        var (response, _) = await Run(new PlatformPingQuery(), Result<int>.Ok(1), TestCurrentUser.Platform(), TestTenantContext.Platform());

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_request_requires_the_permission_when_declared()
    {
        var (response, _) = await Run(new ListTenantsQuery(), Result<int>.Ok(1), TestCurrentUser.Platform(), TestTenantContext.Platform());

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Device_request_passes_for_a_device()
    {
        var (response, _) = await Run(new DeviceHeartbeatCommand(), Result.Success(), TestCurrentUser.Device(TenantA), TestTenantContext.ForTenant(TenantA));

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Device_request_is_forbidden_for_a_user()
    {
        var (response, _) = await Run(new DeviceHeartbeatCommand(), Result.Success(), TestCurrentUser.Tenant(TenantA, "devices.manage"), TestTenantContext.ForTenant(TenantA));

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Device_is_forbidden_on_user_requests()
    {
        var (response, _) = await ReadDevices(TestCurrentUser.Device(TenantA), TestTenantContext.ForTenant(TenantA));

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task Authenticated_user_request_passes_without_permissions()
    {
        var (response, _) = await Run(new GetMeQuery(), Result<string>.Ok("me"), TestCurrentUser.Tenant(TenantA), TestTenantContext.ForTenant(TenantA));

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task System_only_request_is_forbidden_for_users_and_allowed_for_jobs()
    {
        var (forUser, _) = await Run(new SystemJobCommand(), Result.Success(), TestCurrentUser.Platform(), TestTenantContext.Platform());
        var (forSystem, _) = await Run(new SystemJobCommand(), Result.Success(), TestCurrentUser.System(), TestTenantContext.Platform());

        forUser.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
        forSystem.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Background_jobs_running_as_system_pass_permission_checks()
    {
        var (response, _) = await ReadDevices(TestCurrentUser.System(), TestTenantContext.ForTenant(TenantA));

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Request_without_an_authorization_attribute_is_refused()
    {
        var (response, called) = await Run(new UndeclaredQuery(), Result<int>.Ok(1), TestCurrentUser.Platform("platform.tenants.read"), TestTenantContext.Platform());

        response.Error!.Code.ShouldBe("AUTH_FORBIDDEN");
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task Failure_for_a_non_generic_command_is_a_plain_result()
    {
        var (response, _) = await Run(new RetireDeviceCommand(), Result.Success(), TestCurrentUser.Anonymous(), TestTenantContext.Closed());

        response.ShouldBeOfType<Result>();
        response.Error!.Code.ShouldBe("AUTH_UNAUTHORIZED");
    }

    [Fact]
    public void Behavior_is_a_pipeline_behavior() =>
        typeof(AuthorizationBehavior<ReadDevicesQuery, Result<string>>).GetInterfaces()
            .ShouldContain(typeof(IPipelineBehavior<ReadDevicesQuery, Result<string>>));
}
