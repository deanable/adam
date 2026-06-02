using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Adam.Shared.Services
{
    public class ThumbnailService
    {
        public string GetThumbnailPath(string assetId)
        {
            // Ensure the path uses backslashes for Windows compatibility
            return Path.Combine("C:\\thumbnails\\", $"{assetId}.jpg");
        }
    }
}