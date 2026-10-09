using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Reports;

namespace MonitorCloud.Infrastructure.Storage;

/// <summary>The <c>Storage</c> section: where files, Data Protection keys and report work files live, and the PDF browser.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Default: <c>{content root}/App_Data</c>.</summary>
    public string? Root { get; set; }

    /// <summary>A Chromium-based browser for PDF; empty = look for Edge, Chrome or Chromium in the usual places.</summary>
    public string? ChromiumPath { get; set; }
}

internal static class StorageRoot
{
    public static string Of(IOptions<StorageOptions> options, IHostEnvironment environment) =>
        string.IsNullOrWhiteSpace(options.Value.Root) ? Path.Combine(environment.ContentRootPath, "App_Data") : options.Value.Root!;
}

/// <summary>Files under <c>{root}/media/{tenant}/{id}</c> (02 section 11). Paths come from the database row, never from input.</summary>
internal sealed class FileMediaStorage(IOptions<StorageOptions> options, IHostEnvironment environment) : IMediaStorage
{
    private string Full(string storagePath)
    {
        var root = Path.GetFullPath(Path.Combine(StorageRoot.Of(options, environment), "media"));
        var full = Path.GetFullPath(Path.Combine(root, storagePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        return full;
    }

    public async Task WriteAsync(string storagePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var path = Full(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(content, cancellationToken);
    }

    public Task<Stream> OpenAsync(string storagePath, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(Full(storagePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
}

/// <summary>ASP.NET Data Protection with one purpose for stored secrets (02 section 10).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("MonitorCloud.StoredSecrets.v1");

    public string Protect(string plain) => _protector.Protect(plain);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}

/// <summary>
/// Renders print-ready HTML with a headless Chromium (Edge, Chrome or Chromium) through <c>--print-to-pdf</c>
/// (MC-901). Without a browser on the host <see cref="IsAvailable"/> is false and reports are CSV only.
/// </summary>
internal sealed partial class ChromiumPdfRenderer(IOptions<StorageOptions> options, IHostEnvironment environment, ILogger<ChromiumPdfRenderer> logger) : IPdfRenderer
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly Lazy<string?> _browser = new(() => Find(options.Value.ChromiumPath));

    public bool IsAvailable => _browser.Value is not null;

    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        var browser = _browser.Value ?? throw new InvalidOperationException("No Chromium-based browser is available for PDF.");
        var work = Path.Combine(StorageRoot.Of(options, environment), "pdf", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var input = Path.Combine(work, "report.html");
            var output = Path.Combine(work, "report.pdf");
            await File.WriteAllTextAsync(input, html, Encoding.UTF8, cancellationToken);
            var start = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var argument in new[]
                     {
                         "--headless=new", "--disable-gpu", "--no-sandbox", "--no-first-run", "--disable-extensions", "--no-pdf-header-footer",
                         $"--user-data-dir={Path.Combine(work, "profile")}", $"--print-to-pdf={output}", new Uri(input).AbsoluteUri,
                     })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The browser did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("PDF rendering timed out.");
            }

            if (!File.Exists(output))
                throw new InvalidOperationException($"PDF rendering failed (exit code {process.ExitCode}).");
            return await File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException ex)
            {
                LogCleanup(logger, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogCleanup(logger, ex.Message);
            }
        }
    }

    private static string? Find(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;
        string[] candidates = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            ]
            : ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/opt/pw-browsers/chromium", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"];
        return candidates.FirstOrDefault(File.Exists);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "PDF work folder not removed: {Message}")]
    private static partial void LogCleanup(ILogger logger, string message);
}

/// <summary>Posts webhooks with <c>X-Monitor-Signature: sha256=&lt;hex HMAC of the body&gt;</c> (10 s timeout).</summary>
internal sealed class HttpWebhookSender(IHttpClientFactory clients, TimeProvider clock) : IWebhookSender
{
    public const string ClientName = "webhooks";

    public async Task<WebhookResult> SendAsync(string url, string? secret, string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.Add("X-Monitor-Timestamp", timestamp);
        if (!string.IsNullOrEmpty(secret))
        {
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{json}"))).ToLowerInvariant();
            request.Headers.Add("X-Monitor-Signature", $"sha256={signature}");
        }

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MonitorCloud", "1.0"));
        try
        {
            using var response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
            return new WebhookResult(response.IsSuccessStatusCode, (int)response.StatusCode, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return new WebhookResult(false, null, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookResult(false, null, "Timed out.");
        }
    }
}

/// <summary>Generates queued reports one by one (every 5 s; off with the background jobs in tests, which call <see cref="RunOnceAsync"/>).</summary>
public sealed partial class ReportsJob(IServiceScopeFactory scopes, IOptions<AgentSettings> options, TimeProvider clock, ILogger<ReportsJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundJobsEnabled)
            return;
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Generates every queued report; returns how many.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var count = 0;
        while (count < 20)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            if (!await scope.ServiceProvider.GetRequiredService<ReportGenerationService>().RunOneAsync(ct))
                break;
            count++;
        }

        return count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reports job cycle failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
