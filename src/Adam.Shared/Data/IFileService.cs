using Adam.Shared.Models;

namespace Adam.Shared.Data;

public interface IFileService
{
    Task<MetadataProfile> GetMetadataAsync(string filePath, CancellationToken ct = default);
    Task<string> ComputeChecksumAsync(string filePath, CancellationToken ct = default);
    Task<byte[]?> GenerateThumbnailAsync(string filePath, int maxDimension = 256, CancellationToken ct = default);
    Task WriteMetadataAsync(string filePath, MetadataProfile profile, CancellationToken ct = default);
    AssetType GetFileType(string filePath);
    bool IsSupportedFileType(string filePath);
}
