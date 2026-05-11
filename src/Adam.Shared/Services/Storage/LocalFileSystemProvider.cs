namespace Adam.Shared.Services.Storage;

public class LocalFileSystemProvider
{
    public async Task<string> StoreFileAsync(string sourcePath, string storageDirectory, CancellationToken ct)
    {
        var ext = Path.GetExtension(sourcePath);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var destPath = Path.Combine(storageDirectory, fileName);

        Directory.CreateDirectory(storageDirectory);

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destStream = File.Create(destPath);
        await sourceStream.CopyToAsync(destStream, ct);

        return fileName;
    }

    public Task DeleteFileAsync(string relativePath, string storageDirectory, CancellationToken ct)
    {
        var fullPath = Path.Combine(storageDirectory, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetFullPath(string relativePath, string storageDirectory)
    {
        return Path.Combine(storageDirectory, relativePath);
    }
}
