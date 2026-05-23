using Adam.Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Adam.Shared.Services;

/// <summary>
/// Applies rotation and flip transformations to images using ImageSharp.
/// </summary>
public class ImageAdjustmentService
{
    /// <summary>
    /// Applies the specified orientation to an in-memory image.
    /// </summary>
    public void ApplyOrientation(Image image, ImageOrientation orientation)
    {
        switch (orientation)
        {
            case ImageOrientation.Rotate90:
                image.Mutate(x => x.Rotate(RotateMode.Rotate90));
                break;
            case ImageOrientation.Rotate180:
                image.Mutate(x => x.Rotate(RotateMode.Rotate180));
                break;
            case ImageOrientation.Rotate270:
                image.Mutate(x => x.Rotate(RotateMode.Rotate270));
                break;
            case ImageOrientation.FlipHorizontal:
                image.Mutate(x => x.Flip(FlipMode.Horizontal));
                break;
            case ImageOrientation.FlipVertical:
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                break;
        }
    }

    /// <summary>
    /// Rotates the given orientation by 90 degrees clockwise.
    /// </summary>
    public static ImageOrientation Rotate90Cw(ImageOrientation current)
    {
        return current switch
        {
            ImageOrientation.Normal => ImageOrientation.Rotate90,
            ImageOrientation.Rotate90 => ImageOrientation.Rotate180,
            ImageOrientation.Rotate180 => ImageOrientation.Rotate270,
            ImageOrientation.Rotate270 => ImageOrientation.Normal,
            ImageOrientation.FlipHorizontal => ImageOrientation.FlipHorizontal, // Simplification: flips remain flips
            ImageOrientation.FlipVertical => ImageOrientation.FlipVertical,
            _ => ImageOrientation.Normal
        };
    }

    /// <summary>
    /// Rotates the given orientation by 90 degrees counter-clockwise.
    /// </summary>
    public static ImageOrientation Rotate90Ccw(ImageOrientation current)
    {
        return current switch
        {
            ImageOrientation.Normal => ImageOrientation.Rotate270,
            ImageOrientation.Rotate90 => ImageOrientation.Normal,
            ImageOrientation.Rotate180 => ImageOrientation.Rotate90,
            ImageOrientation.Rotate270 => ImageOrientation.Rotate180,
            ImageOrientation.FlipHorizontal => ImageOrientation.FlipHorizontal,
            ImageOrientation.FlipVertical => ImageOrientation.FlipVertical,
            _ => ImageOrientation.Normal
        };
    }

    /// <summary>
    /// Toggles horizontal flip on the given orientation.
    /// </summary>
    public static ImageOrientation ToggleFlipHorizontal(ImageOrientation current)
    {
        return current == ImageOrientation.FlipHorizontal
            ? ImageOrientation.Normal
            : ImageOrientation.FlipHorizontal;
    }

    /// <summary>
    /// Toggles vertical flip on the given orientation.
    /// </summary>
    public static ImageOrientation ToggleFlipVertical(ImageOrientation current)
    {
        return current == ImageOrientation.FlipVertical
            ? ImageOrientation.Normal
            : ImageOrientation.FlipVertical;
    }
}
