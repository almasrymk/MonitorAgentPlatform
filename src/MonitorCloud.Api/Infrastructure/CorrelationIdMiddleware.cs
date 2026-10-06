using System.Text.RegularExpressions;
using Serilog.Context;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>Accepts <c>X-Correlation-Id</c> (or creates one), returns it and adds it to every log event.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(incoming) ? incoming : Guid.CreateVersion7().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
            await next(context);
    }

    /// <summary>Only short, safe identifiers are echoed back (no header injection, no log forging).</summary>
    internal static bool IsValid(string value) => value.Length is > 0 and <= 64 && SafeId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial Regex SafeId();
}
