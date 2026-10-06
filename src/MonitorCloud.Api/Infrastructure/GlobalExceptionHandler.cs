using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>Domain exceptions become their problem; anything else is a bug: 500 <c>INTERNAL_ERROR</c> without details.</summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ProblemDetails problem;
        switch (exception)
        {
            case DomainException domain:
                problem = ProblemDetailsEnricher.FromError(domain.Error, httpContext);
                break;
            case MonitorCloud.Application.Abstractions.Persistence.ConcurrencyConflictException:
                problem = ProblemDetailsEnricher.FromError(MonitorCloud.Application.Common.CommonErrors.ConcurrencyConflict, httpContext);
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "Bad Request", Detail = "The request is malformed." };
                problem.Extensions[ProblemDetailsEnricher.CodeKey] = "BAD_REQUEST";
                break;
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                httpContext.Response.StatusCode = 499;
                return true;
            default:
                LogUnhandled(logger, httpContext.Request.Method, httpContext.Request.Path, exception);
                problem = new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Internal Server Error", Detail = "An unexpected error occurred." };
                problem.Extensions[ProblemDetailsEnricher.CodeKey] = "INTERNAL_ERROR";
                break;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, string method, string path, Exception exception);
}
