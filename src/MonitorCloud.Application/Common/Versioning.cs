namespace MonitorCloud.Application.Common;

/// <summary>Row versions travel as base64 strings (ETag / If-Match, 06 section 7).</summary>
public static class Versioning
{
    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion ?? []);

    public static byte[]? Decode(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        var value = version.Trim().Trim('"');
        if (value.StartsWith("W/", StringComparison.Ordinal))
            value = value[2..].Trim('"');
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
