using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MonitorCloud.Api.Infrastructure;

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Exception is null ? e.Value.Description : "Check failed.", data = e.Value.Data }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
