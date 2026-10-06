using MediatR;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Behaviors;

/// <summary>
/// 5. Commands only: one <c>SaveChangesAsync</c> after a successful handler. Audit records and outbox messages
/// are written by the same save, in the same transaction. A failed command saves nothing.
/// </summary>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not IBaseCommand)
            return await next(cancellationToken);

        TResponse response;
        try
        {
            response = await next(cancellationToken);
        }
        catch
        {
            unitOfWork.DiscardChanges();
            throw;
        }

        if (response.IsFailure)
        {
            unitOfWork.DiscardChanges();
            return response;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
