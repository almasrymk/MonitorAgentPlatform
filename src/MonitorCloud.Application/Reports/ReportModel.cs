using System.Globalization;
using System.Net;
using System.Text;

namespace MonitorCloud.Application.Reports;

/// <summary>A report type of the Reports screen (07 section 5.8) and the plan feature it needs (04 section 2).</summary>
public sealed record ReportType(string Type, string Title, string Feature, bool Advanced);

public static class ReportCatalog
{
    public const string Basic = "reports.basic";
    public const string Advanced = "reports.advanced";

    public static readonly IReadOnlyList<ReportType> All =
    [
        new("overview", "Overview Report", Basic, false),
        new("location-summary", "Location Summary", Basic, false),
        new("device-health", "Device Health", Basic, false),
        new("performance", "Performance (CPU/RAM/Disk)", Advanced, true),
        new("network-usage", "Network Usage", Advanced, true),
        new("alerts", "Alerts & Incidents", Basic, false),
        new("license-usage", "License Usage", Basic, false),
        new("custom", "Custom Report", Advanced, true),
    ];

    public static ReportType? Find(string? type) => All.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// What the user chose on the Reports screen. <paramref name="Scope"/> is the location scope of a restricted requester: the report is
/// generated in that scope, so it never shows more than the requester may see.
/// </summary>
public sealed record ReportParameters(
    IReadOnlyList<Guid> LocationIds, IReadOnlyList<Guid> DeviceIds, DateTimeOffset From, DateTimeOffset To, string GroupBy, IReadOnlyList<Guid>? Scope = null);

public sealed record ReportColumn(string Key, string Label, bool Numeric = false);

public sealed record ReportSummaryItem(string Label, string Value);

/// <summary>The content of a report: a summary and one table. CSV and PDF are both rendered from it.</summary>
public sealed record ReportData(string Title, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReportSummaryItem> Summary, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

public static class ReportRenderer
{
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>RFC 4180 CSV (UTF-8 with BOM so spreadsheet programs read Arabic names).</summary>
    public static byte[] Csv(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', data.Columns.Select(c => Escape(c.Label))));
        foreach (var row in data.Rows)
            csv.AppendLine(string.Join(',', row.Select(v => Escape(v is string text ? Neutralize(text) : Format(v)))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>Print-ready HTML (A4, no external resources) for the PDF step.</summary>
    public static string Html(ReportData data, string customerName)
    {
        ArgumentNullException.ThrowIfNull(data);
        var h = new StringBuilder();
        h.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode(data.Title)).Append("</title><style>")
            .Append("@page{size:A4;margin:14mm}body{font-family:Segoe UI,Arial,sans-serif;font-size:10pt;color:#1a2230}h1{font-size:16pt;margin:0 0 4px}")
            .Append(".meta{color:#5b6778;margin-bottom:12px}.summary{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:14px}.summary div{border:1px solid #d5dbe3;border-radius:6px;padding:6px 10px}")
            .Append(".summary b{display:block;font-size:12pt}table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #e3e7ec;padding:4px 6px;text-align:left}")
            .Append("th{background:#f2f4f7}td.n{text-align:right}tr{page-break-inside:avoid}</style></head><body>");
        h.Append("<h1>").Append(WebUtility.HtmlEncode(data.Title)).Append("</h1><div class=\"meta\">").Append(WebUtility.HtmlEncode(customerName)).Append(" · ")
            .Append(Format(data.From)).Append(" - ").Append(Format(data.To)).Append(" UTC</div><div class=\"summary\">");
        foreach (var item in data.Summary)
            h.Append("<div>").Append(WebUtility.HtmlEncode(item.Label)).Append("<b>").Append(WebUtility.HtmlEncode(item.Value)).Append("</b></div>");
        h.Append("</div><table><thead><tr>");
        foreach (var c in data.Columns)
            h.Append("<th>").Append(WebUtility.HtmlEncode(c.Label)).Append("</th>");
        h.Append("</tr></thead><tbody>");
        foreach (var row in data.Rows)
        {
            h.Append("<tr>");
            for (var i = 0; i < data.Columns.Count; i++)
                h.Append(data.Columns[i].Numeric ? "<td class=\"n\">" : "<td>").Append(WebUtility.HtmlEncode(Format(i < row.Count ? row[i] : null))).Append("</td>");
            h.Append("</tr>");
        }

        h.Append("</tbody></table></body></html>");
        return h.ToString();
    }

    /// <summary>Text that a spreadsheet would read as a formula (CSV injection) gets a leading apostrophe.</summary>
    public static string Neutralize(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
