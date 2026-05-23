namespace Adam.Shared.Models;

public enum AssetLabel
{
    None = 0,
    Red = 1,
    Green = 2,
    Blue = 3,
    Yellow = 4,
    Purple = 5
}

public enum AssetFlag
{
    Unflagged = 0,
    Pick = 1,
    Reject = 2
}

public enum ImageOrientation
{
    Normal = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3,
    FlipHorizontal = 4,
    FlipVertical = 5
}
