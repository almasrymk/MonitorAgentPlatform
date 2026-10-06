using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Api.Infrastructure;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Controllers;

/// <summary>Controllers contain no logic: bind, <see cref="ISender.Send"/>, map the <see cref="Result"/> to HTTP.</summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public abstract class ApiControllerBase(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    /// <summary>The <c>If-Match</c> header (row version) for optimistic concurrency.</summary>
    protected string? IfMatch => Request.Headers.IfMatch.ToString() is { Length: > 0 } value ? value : null;

    protected IActionResult FromResult(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected IActionResult FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    /// <summary>200 with an <c>ETag</c> taken from the DTO's version.</summary>
    protected IActionResult FromVersioned<T>(Result<T> result, Func<T, string> version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (result.IsFailure)
            return Problem(result.Error!);
        Response.Headers.ETag = $"\"{version(result.Value)}\"";
        return Ok(result.Value);
    }

    protected IActionResult Created<T>(Result<T> result) =>
        result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error!);

    protected IActionResult Problem(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var problem = ProblemDetailsEnricher.FromError(error, HttpContext);
        ProblemDetailsEnricher.Enrich(new ProblemDetailsContext { HttpContext = HttpContext, ProblemDetails = problem });
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>Marks a list endpoint (paged, sortable); the generic paging suite covers every endpoint with it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ListEndpointAttribute : Attribute;
