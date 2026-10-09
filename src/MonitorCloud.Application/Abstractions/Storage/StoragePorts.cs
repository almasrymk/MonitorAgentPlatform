namespace MonitorCloud.Application.Abstractions.Storage;

/// <summary>A file the API streams to the client (report outputs, archive attachments).</summary>
public sealed record DownloadDto(string FileName, string ContentType, Stream Content);

/// <summary>Writes and reads the bytes of <c>media.MediaFiles</c> rows (Infrastructure: <c>App_Data/media/{tenant}/{id}</c>).</summary>
public interface IMediaStorage
{
    Task WriteAsync(string storagePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(string storagePath, CancellationToken cancellationToken);
}

/// <summary>Encrypts values that must be read back (remote-access entries, webhook secrets) with ASP.NET Data Protection.</summary>
public interface ISecretProtector
{
    string Protect(string plain);

    string Unprotect(string protectedValue);
}

/// <summary>Print-ready HTML to PDF (a headless Chromium on the host); <see cref="IsAvailable"/> is false when none is installed.</summary>
public interface IPdfRenderer
{
    bool IsAvailable { get; }

    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken);
}

public sealed record WebhookResult(bool Success, int? StatusCode, string? Error);

/// <summary>Posts a JSON payload signed with HMAC-SHA256 (<c>X-Monitor-Signature</c>).</summary>
public interface IWebhookSender
{
    Task<WebhookResult> SendAsync(string url, string? secret, string json, CancellationToken cancellationToken);
}
