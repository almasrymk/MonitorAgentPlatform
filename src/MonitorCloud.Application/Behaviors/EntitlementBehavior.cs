using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Common;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Behaviors;

/// <summary>
/// 4. For requests marked <see cref="RequiresFeatureAttribute"/>: the tenant's plan must include every feature
/// (<c>FEATURE_NOT_ENTITLED</c>), and such commands are refused when the subscription has expired (<c>ENTITLEMENT_EXPIRED</c>).
/// </summary>
public sealed class EntitlementBehavior<TRequest, TResponse>(ITenantContext tenantContext, IEntitlementReader entitlements)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly ConcurrentDictionary<Type, string[]> Features = new();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var required = Features.GetOrAdd(typeof(TRequest),
            t => t.GetCustomAttributes<RequiresFeatureAttribute>(false).Select(a => a.Feature).ToArray());

        // Platform scope without a workspace is not limited by a customer's plan.
        if (required.Length == 0 || tenantContext.TenantId is not { } tenantId)
            return await next(cancellationToken);

        var snapshot = await entitlements.GetAsync(tenantId, cancellationToken);
        var missing = required.FirstOrDefault(f => !snapshot.Has(f));
        if (missing is not null)
            return ResultFactory.Failure<TResponse>(CommonErrors.FeatureNotEntitled(missing));

        if (snapshot.IsExpired && request is IBaseCommand)
            return ResultFactory.Failure<TResponse>(CommonErrors.EntitlementExpired);

        return await next(cancellationToken);
    }
}
