namespace MonitorCloud.Application.Media.Contracts;

public sealed record StoredMedia(Guid Id, string FileName, string ContentType, long SizeBytes);

public sealed record MediaContent(string FileName, string ContentType, long SizeBytes, Stream Content);

/// <summary>Stores and reads files for other modules (reports, archive). Validation: allowed types, 10 MB (02 section 11).</summary>
public interface IMediaFiles
{
    /// <summary>Validates and stores the file; the row joins the current unit of work. Throws a validation <c>DomainException</c> for a bad file.</summary>
    Task<StoredMedia> SaveAsync(Guid? tenantId, string fileName, byte[] content, CancellationToken ct);

    /// <summary>The file in the caller's scope, or null.</summary>
    Task<MediaContent?> OpenAsync(Guid mediaId, CancellationToken ct);

    Task DeleteAsync(Guid mediaId, CancellationToken ct);
}
