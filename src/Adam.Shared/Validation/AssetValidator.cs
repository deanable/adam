namespace Adam.Shared.Validation;

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public class AssetValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tiff", ".tif",
        ".cr2", ".nef", ".arw", ".dng",
        ".mp4", ".mov",
        ".pdf", ".docx", ".txt",
        ".mp3", ".wav"
    };

    private const long MaxFileSize = 2L * 1024 * 1024 * 1024;
    private const int MaxTitleLength = 200;
    private const int MaxTagsCount = 20;
    private const int MaxDescriptionLength = 2000;

    public ValidationResult ValidateForIngestion(
        string filePath,
        long fileSize,
        string title,
        string[] tags,
        string? description)
    {
        var errors = new List<string>();

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            errors.Add($"File type '{ext}' is not supported.");
        }

        if (fileSize > MaxFileSize)
        {
            errors.Add("File size exceeds the 2 GB limit.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Title is required.");
        }
        else if (title.Length > MaxTitleLength)
        {
            errors.Add($"Title must be {MaxTitleLength} characters or fewer.");
        }

        if (tags.Length > MaxTagsCount)
        {
            errors.Add($"A maximum of {MaxTagsCount} tags are allowed.");
        }

        if (description?.Length > MaxDescriptionLength)
        {
            errors.Add($"Description must be {MaxDescriptionLength} characters or fewer.");
        }

        return new ValidationResult(errors.Count == 0, errors.AsReadOnly());
    }
}
