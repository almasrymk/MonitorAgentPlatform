using MediatR;
using Microsoft.Extensions.Logging;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Behaviors;

/// <summary>1. Request name, tenant, user and duration; warns above 500 ms. Never logs the request body.</summary>
public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    internal static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(500);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var name = typeof(TRequest).Name;
        var started = timeProvider.GetTimestamp();
        var response = await next(cancellationToken);
        var elapsed = timeProvider.GetElapsedTime(started);

        var actor = currentUser.UserId ?? currentUser.DeviceId;
        if (elapsed > SlowThreshold)
            LogSlow(logger, name, tenantContext.TenantId, actor, elapsed.TotalMilliseconds);
        else
            LogHandled(logger, name, tenantContext.TenantId, actor, elapsed.TotalMilliseconds);

        if (response.IsFailure)
            LogFailed(logger, name, response.Error!.Code);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Request} tenant={TenantId} actor={ActorId} in {ElapsedMs:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string request, Guid? tenantId, Guid? actorId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slow request {Request} tenant={TenantId} actor={ActorId} took {ElapsedMs:0.0} ms")]
    private static partial void LogSlow(ILogger logger, string request, Guid? tenantId, Guid? actorId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} failed with {Code}")]
    private static partial void LogFailed(ILogger logger, string request, string code);
}
