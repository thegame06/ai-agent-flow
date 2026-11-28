using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace AgentFlow.Api.Channels;

/// <summary>A file a customer sent (a receipt photo, a PDF), stored by AgentFlow.</summary>
public sealed record StoredMedia(string Id, string ContentType, string? FileName, long Size);

public interface IMediaStore
{
    Task<StoredMedia> SaveAsync(string tenantId, byte[] content, string contentType, string? fileName, string source, CancellationToken ct = default);
    /// <summary>The file, only for its own tenant.</summary>
    Task<(byte[] Content, StoredMedia Info)?> GetAsync(string tenantId, string mediaId, CancellationToken ct = default);
}

/// <summary>Files in MongoDB GridFS (bucket "channel_media"), tagged with their tenant.</summary>
public sealed class MongoMediaStore : IMediaStore
{
    /// <summary>The largest file kept (WhatsApp's own limit for documents is 100 MB; receipts are far smaller).</summary>
    public const long MaxBytes = 16 * 1024 * 1024;

    private readonly GridFSBucket _bucket;

    public MongoMediaStore(IMongoDatabase database)
    {
        _bucket = new GridFSBucket(database, new GridFSBucketOptions { BucketName = "channel_media" });
    }

    public async Task<StoredMedia> SaveAsync(string tenantId, byte[] content, string contentType, string? fileName, string source, CancellationToken ct = default)
    {
        if (content.Length > MaxBytes) throw new InvalidOperationException($"El archivo pesa mas de {MaxBytes / (1024 * 1024)} MB.");
        var name = string.IsNullOrWhiteSpace(fileName) ? $"archivo{ExtensionFor(contentType)}" : fileName!;
        var id = await _bucket.UploadFromBytesAsync(name, content, new GridFSUploadOptions
        {
            Metadata = new BsonDocument
            {
                { "tenantId", tenantId },
                { "contentType", contentType },
                { "source", source }
            }
        }, ct);
        return new StoredMedia(id.ToString(), contentType, name, content.Length);
    }

    public async Task<(byte[] Content, StoredMedia Info)?> GetAsync(string tenantId, string mediaId, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(mediaId, out var id)) return null;
        var filter = Builders<GridFSFileInfo>.Filter.And(
            Builders<GridFSFileInfo>.Filter.Eq("_id", id),
            Builders<GridFSFileInfo>.Filter.Eq("metadata.tenantId", tenantId));
        using var cursor = await _bucket.FindAsync(filter, cancellationToken: ct);
        var info = await cursor.FirstOrDefaultAsync(ct);
        if (info is null) return null;
        var bytes = await _bucket.DownloadAsBytesAsync(id, cancellationToken: ct);
        var contentType = info.Metadata?.GetValue("contentType", "application/octet-stream").AsString ?? "application/octet-stream";
        return (bytes, new StoredMedia(mediaId, contentType, info.Filename, info.Length));
    }

    public static string ExtensionFor(string? contentType) => (contentType ?? string.Empty).ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "application/pdf" => ".pdf",
        "audio/ogg" or "audio/ogg; codecs=opus" => ".ogg",
        "audio/mpeg" => ".mp3",
        "video/mp4" => ".mp4",
        _ => string.Empty
    };
}
