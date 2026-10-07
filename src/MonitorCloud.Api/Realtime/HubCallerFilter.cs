using Microsoft.AspNetCore.SignalR;

namespace MonitorCloud.Api.Realtime;

/// <summary>
/// Hub invocations do not run inside an HTTP request of their own: this filter points
/// <see cref="IHttpContextAccessor"/> at the connection's request, so <c>ICurrentUser</c> sees the signed-in caller.
/// </summary>
public sealed class HubCallerFilter(IHttpContextAccessor accessor) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);
        accessor.HttpContext = invocationContext.Context.GetHttpContext();
        return await next(invocationContext);
    }
}