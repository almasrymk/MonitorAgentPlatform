using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Media;

/// <summary>A stored file (02 section 11): report outputs and archive attachments. Bytes live under <c>App_Data/media/{tenant}/{id}</c>.</summary>
public sealed class MediaFile : Entity, IOptionallyTenantOwned
{
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>Allowed content types by extension (09 section 2: upload validation).</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".zip"] = "application/zip",
    };

    public static readonly Error TooLarge = Error.PayloadTooLarge("UPLOAD_TOO_LARGE", "Files may have at most 10 MB.");
    public static readonly Error TypeNotAllowed = Error.UnsupportedMediaType("UPLOAD_TYPE_NOT_ALLOWED", "This file type is not allowed.");

    /// <summary>
    /// The last segment of a client file name, whatever the separator (<c>..\..\x.pdf</c> and <c>../x.pdf</c> become <c>x.pdf</c>);
    /// control characters are removed and a name of only dots is refused.
    /// </summary>
    public static string SafeName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var last = fileName[(fileName.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        var clean = new string(last.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"' or '|' or '?' or '*')).ToArray()).Trim();
        Guard.Against(clean.Trim('.').Length == 0, Error.Validation("UPLOAD_NAME_INVALID", "The file name is not valid."));
        return clean;
    }

    private MediaFile()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        StoragePath = string.Empty;
    }

    public Guid? TenantId { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; }
    public string StoragePath { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaFile Create(Guid? tenantId, string fileName, long sizeBytes, string sha256, DateTimeOffset now)
    {
        var name = SafeName(Guard.NotEmpty(fileName, nameof(FileName), 200));
        var extension = Path.GetExtension(name);
        Guard.Against(!AllowedTypes.TryGetValue(extension, out var contentType), TypeNotAllowed);
        Guard.Against(sizeBytes <= 0, Error.Validation("UPLOAD_EMPTY", "The file is empty."));
        Guard.Against(sizeBytes > MaxBytes, TooLarge);
        var file = new MediaFile
        {
            TenantId = tenantId,
            FileName = name,
            ContentType = contentType!,
            SizeBytes = sizeBytes,
            Sha256 = Guard.NotEmpty(sha256, nameof(Sha256), 64),
            CreatedAt = now,
        };
        file.StoragePath = $"{tenantId?.ToString("N") ?? "platform"}/{file.Id:N}";
        return file;
    }
}
