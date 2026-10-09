using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Media.Contracts;
using MonitorCloud.Domain.Media;

namespace MonitorCloud.Application.Media;

internal sealed class MediaFiles(IAppDbContext db, IMediaStorage storage, TimeProvider clock) : IMediaFiles
{
    public async Task<StoredMedia> SaveAsync(Guid? tenantId, string fileName, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var file = MediaFile.Create(tenantId, fileName, content.LongLength, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), clock.GetUtcNow());
        await storage.WriteAsync(file.StoragePath, content, ct);
        db.Set<MediaFile>().Add(file);
        return new StoredMedia(file.Id, file.FileName, file.ContentType, file.SizeBytes);
    }

    public async Task<MediaContent?> OpenAsync(Guid mediaId, CancellationToken ct)
    {
        var file = await db.Set<MediaFile>().AsNoTracking().SingleOrDefaultAsync(m => m.Id == mediaId, ct);
        return file is null ? null : new MediaContent(file.FileName, file.ContentType, file.SizeBytes, await storage.OpenAsync(file.StoragePath, ct));
    }

    public async Task DeleteAsync(Guid mediaId, CancellationToken ct)
    {
        var file = await db.Set<MediaFile>().SingleOrDefaultAsync(m => m.Id == mediaId, ct);
        if (file is not null)
            db.Set<MediaFile>().Remove(file);
    }
}
