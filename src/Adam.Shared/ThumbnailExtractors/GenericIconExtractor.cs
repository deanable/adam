using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.Fonts;

namespace Adam.Shared.ThumbnailExtractors
{
    public class GenericIconExtractor
    {
        public async Task ExtractAsync(string sourcePath, string destPath, int maxSize, CancellationToken ct)
        {
            if (!File.Exists(sourcePath))
            {
                // Handle the case where the source file does not exist
                return;
            }

            // Load the font, ensuring that a fallback is used if Arial is not available
            FontFamily fontFamily;
            try
            {
                fontFamily = SystemFonts.Find("Arial");
            }
            catch (FontFamilyNotFoundException)
            {
                fontFamily = SystemFonts.Find("Segoe UI"); // Fallback font
            }

            using (var image = new Bitmap(sourcePath))
            {
                // Resize and save the image logic here
                using (var resizedImage = new Bitmap(image, new Size(maxSize, maxSize)))
                {
                    resizedImage.Save(destPath, ImageFormat.Jpeg);
                }
            }
        }
    }
}