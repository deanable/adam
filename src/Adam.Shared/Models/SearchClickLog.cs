namespace Adam.Shared.Models;

/// <summary>
/// Records a user click on a search result, used by SearchRankingService
/// to re-rank results based on implicit click feedback.
/// </summary>
public sealed class SearchClickLog
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string? NormalizedQuery { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public int DwellTimeMs { get; set; }
    public int RankPosition { get; set; }
    public Guid? UserId { get; set; }

    public DigitalAsset Asset { get; set; } = null!;
    public User? User { get; set; }
}
