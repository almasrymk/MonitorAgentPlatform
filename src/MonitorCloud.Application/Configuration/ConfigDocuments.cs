using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorCloud.Application.Configuration;

public sealed record TelemetrySettings(int SampleSeconds = 5);

/// <summary>A usage threshold: Warning and Critical levels held for <see cref="ForSeconds"/>, cleared below <see cref="ClearBelowPercent"/>.</summary>
public sealed record UsageThreshold(double WarningPercent, double CriticalPercent, int ForSeconds, double? ClearBelowPercent = null);

public sealed record TemperatureThreshold(double Critical, int ForSeconds);

public sealed record Thresholds(UsageThreshold Cpu, UsageThreshold Ram, UsageThreshold Disk, TemperatureThreshold TempC);

public sealed record FeatureSettings(bool RemoteActions = false);

/// <summary>The part of the device configuration document (05 section 8) edited in the portal; monitor points are added when it is sent.</summary>
public sealed record ConfigDocument(TelemetrySettings Telemetry, Thresholds Thresholds, FeatureSettings Features);

/// <summary>A monitor point as the agent receives it (05 section 8). Secrets never travel: <see cref="SecretRef"/> names one stored on the device.</summary>
public sealed record AgentPointDocument(
    string Key, string DisplayName, string Type, string Target, int IntervalSeconds, string AlertLevel, bool Enabled, bool ShowInShortcut, JsonElement? Settings, string? SecretRef);

/// <summary>The complete document of <c>ConfigUpdate</c>.</summary>
public sealed record AgentConfigDocument(int Version, TelemetrySettings Telemetry, Thresholds Thresholds, IReadOnlyList<AgentPointDocument> MonitorPoints, FeatureSettings Features);

public static class ConfigDocuments
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The values of 05 section 8, used when a tenant has no defaults of its own.</summary>
    public static ConfigDocument Default { get; } = new(
        new TelemetrySettings(5),
        new Thresholds(
            new UsageThreshold(80, 95, 300, 75),
            new UsageThreshold(80, 95, 300, 75),
            new UsageThreshold(85, 92, 60),
            new TemperatureThreshold(85, 120)),
        new FeatureSettings(false));

    public static string Serialize<T>(T document) => JsonSerializer.Serialize(document, Json);

    public static ConfigDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ConfigDocument>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Schema rules (09 section 2): field errors keyed by their JSON path; empty = valid.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(ConfigDocument? document)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (document?.Telemetry is null || document.Thresholds is null || document.Features is null)
        {
            errors["document"] = ["telemetry, thresholds and features are required."];
            return errors;
        }

        if (document.Telemetry.SampleSeconds is < 1 or > 30)
            errors["telemetry.sampleSeconds"] = ["Must be 1-30 seconds."];
        Usage(errors, "thresholds.cpu", document.Thresholds.Cpu);
        Usage(errors, "thresholds.ram", document.Thresholds.Ram);
        Usage(errors, "thresholds.disk", document.Thresholds.Disk);
        if (document.Thresholds.TempC is null)
            errors["thresholds.tempC"] = ["Required."];
        else
        {
            if (document.Thresholds.TempC.Critical is < 30 or > 120)
                errors["thresholds.tempC.critical"] = ["Must be 30-120 °C."];
            if (document.Thresholds.TempC.ForSeconds is < 0 or > 3600)
                errors["thresholds.tempC.forSeconds"] = ["Must be 0-3600 seconds."];
        }

        return errors;
    }

    private static void Usage(Dictionary<string, string[]> errors, string path, UsageThreshold? t)
    {
        if (t is null)
        {
            errors[path] = ["Required."];
            return;
        }

        if (t.WarningPercent is <= 0 or > 100)
            errors[$"{path}.warningPercent"] = ["Must be 1-100."];
        if (t.CriticalPercent is <= 0 or > 100)
            errors[$"{path}.criticalPercent"] = ["Must be 1-100."];
        else if (t.CriticalPercent <= t.WarningPercent)
            errors[$"{path}.criticalPercent"] = ["Must be above the warning level."];
        if (t.ForSeconds is < 0 or > 3600)
            errors[$"{path}.forSeconds"] = ["Must be 0-3600 seconds."];
        if (t.ClearBelowPercent is { } clear && (clear <= 0 || clear >= t.WarningPercent))
            errors[$"{path}.clearBelowPercent"] = ["Must be below the warning level."];
    }
}
