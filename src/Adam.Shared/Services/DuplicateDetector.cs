using Microsoft.EntityFrameworkCore;
using Adam.Shared.Data;
using Adam.Shared.Models;

namespace Adam.Shared.Services;

public class DuplicateDetector
{
    private readonly ChecksumService _checksumService;

    public DuplicateDetector(ChecksumService checksumService)
    {
        _checksumService = checksumService;
    }

    public async Task<DigitalAsset?> FindDuplicateAsync(
        string filePath,
        AppDbContext dbContext,
        CancellationToken ct = default)
    {
        var hash = await _checksumService.ComputeSha256Async(filePath, ct);
        return await dbContext.DigitalAssets
            .FirstOrDefaultAsync(a => a.ChecksumSha256 == hash, ct);
    }
}
