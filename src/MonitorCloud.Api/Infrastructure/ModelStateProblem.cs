using Microsoft.AspNetCore.Mvc;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>Model-binding errors use the same format as FluentValidation failures (<c>VALIDATION_FAILED</c>).</summary>
public static class ModelStateProblem
{
    public static IActionResult Create(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = context.ModelState
            .Where(e => e.Value is { Errors.Count: > 0 })
            .ToDictionary(
                e => string.IsNullOrEmpty(e.Key) ? "body" : char.ToLowerInvariant(e.Key[0]) + e.Key[1..],
                e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "The value is invalid." : x.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "One or more fields are invalid.",
        };
        problem.Extensions[ProblemDetailsEnricher.CodeKey] = "VALIDATION_FAILED";
        ProblemDetailsEnricher.Enrich(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
    }
}
