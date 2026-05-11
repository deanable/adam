namespace Adam.Shared.Models;

public enum ColorLabel
{
    None,
    Red,
    Green,
    Blue,
    Yellow,
    Purple
}

public enum FlagStatus
{
    None,
    Pick,
    Reject
}

public class RatingInfo
{
    public Guid Id { get; set; }
    public Guid DigitalAssetId { get; set; }
    public int Stars { get; set; }
    public ColorLabel ColorLabel { get; set; }
    public FlagStatus Flag { get; set; }

    public DigitalAsset? DigitalAsset { get; set; }
}
