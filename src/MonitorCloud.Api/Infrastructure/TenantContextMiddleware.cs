using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>
/// Resolves the tenant scope of the request after authentication (03 section 3):
/// tenant users get their own tenant and location scope; platform users are unrestricted, or inside the workspace
/// named by <c>X-Tenant-Id</c>; devices get their tenant; anonymous requests stay closed.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-Id";

    public async Task InvokeAsync(HttpContext context, ICurrentUser user, ITenantScopeSetter scope, ITenantDirectory tenants, IAuditLogger audit)
    {
        ArgumentNullException.ThrowIfNull(context);
        var header = context.Request.Headers[HeaderName].ToString();

        if (user.IsAuthenticated && user.ActorType == ActorType.Device && user.TenantId is { } deviceTenant)
        {
            scope.RunAsTenant(deviceTenant);
        }
        else if (user.IsAuthenticated && user.ActorType == ActorType.User)
        {
            if (user.TenantId is { } tenantId)
            {
                if (header.Length > 0 && (!Guid.TryParse(header, out var requested) || requested != tenantId))
                {
                    await audit.WriteNowAsync("tenant.cross_attempt", "Tenant", header.Length > 64 ? header[..64] : header, $"Tenant user of {tenantId} sent {HeaderName}", false, context.RequestAborted);
                    await WriteProblemAsync(context, Error.Forbidden("AUTH_FORBIDDEN", "You do not have access to that customer."));
                    return;
                }

                scope.RunAsTenant(tenantId, user.LocationScope);
            }
            else if (Roles.IsPlatformRole(user.Role))
            {
                if (header.Length == 0)
                {
                    scope.RunAsSystem();
                }
                else if (Guid.TryParse(header, out var workspace) && await tenants.FindAsync(workspace, context.RequestAborted) is { Status: not "Archived" })
                {
                    scope.RunAsTenant(workspace);
                }
                else
                {
                    await WriteProblemAsync(context, Error.NotFound("TENANT_NOT_FOUND", "Customer not found."));
                    return;
                }
            }
        }

        await next(context);
    }

    private static async Task WriteProblemAsync(HttpContext context, Error error)
    {
        var problem = ProblemDetailsEnricher.FromError(error, context);
        context.Response.StatusCode = problem.Status ?? 400;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });
    }
}

