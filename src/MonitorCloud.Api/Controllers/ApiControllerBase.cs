using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Api.Infrastructure;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Controllers;

/// <summary>Controllers contain no logic: bind, <see cref="ISender.Send"/>, map the <see cref="Result"/> to HTTP.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    protected IActionResult FromResult(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected IActionResult FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    protected IActionResult Problem(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var problem = ProblemDetailsEnricher.FromError(error, HttpContext);
        ProblemDetailsEnricher.Enrich(new ProblemDetailsContext { HttpContext = HttpContext, ProblemDetails = problem });
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}
