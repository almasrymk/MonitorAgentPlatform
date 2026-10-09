using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>Every problem response carries <c>code</c> and <c>traceId</c> (RFC 7807, 01 section 7).</summary>
public static class ProblemDetailsEnricher
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static void Enrich(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        problem.Status = status;
        problem.Extensions.TryAdd(CodeKey, DefaultCode(status));
        problem.Extensions[TraceIdKey] = TraceId(context.HttpContext);
        problem.Instance ??= context.HttpContext.Request.Path;
    }

    public static string TraceId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Activity.Current?.Id ?? context.TraceIdentifier;
    }

    public static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "BAD_REQUEST",
        StatusCodes.Status401Unauthorized => "AUTH_UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "AUTH_FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status405MethodNotAllowed => "METHOD_NOT_ALLOWED",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status412PreconditionFailed => "PRECONDITION_FAILED",
        StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        >= 500 => "INTERNAL_ERROR",
        _ => "ERROR",
    };

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Locked => StatusCodes.Status423Locked,
        ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorKind.Gone => StatusCodes.Status410Gone,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorKind.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>Builds the problem for a domain <see cref="Error"/>.</summary>
    public static ProblemDetails FromError(Error error, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(context);
        var status = StatusFor(error.Kind);
        var problem = error.FieldErrors is { Count: > 0 }
            ? new ValidationProblemDetails(error.FieldErrors.ToDictionary(k => k.Key, k => k.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = ReasonPhrases.GetReasonPhrase(status);
        problem.Detail = error.Message;
        problem.Extensions[CodeKey] = error.Code;
        if (error.Details is not null)
        {
            foreach (var (key, value) in error.Details)
                problem.Extensions[key] = value;
        }

        return problem;
    }
}

internal static class ReasonPhrases
{
    public static string GetReasonPhrase(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
}
