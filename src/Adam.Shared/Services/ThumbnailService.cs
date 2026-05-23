using System.Runtime.InteropServices;
using Adam.Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Adam.Shared.Services;

public class ThumbnailService
{
    private readonly ImageAdjustmentService _adjustment = new();

    public Task<string> GenerateThumbnailAsync(string sourcePath, string thumbnailDirectory, CancellationToken ct = default)
        => GenerateThumbnailAsync(sourcePath, thumbnailDirectory, ImageOrientation.Normal, ct);

    public async Task<string> GenerateThumbnailAsync(string sourcePath, string thumbnailDirectory, ImageOrientation orientation, CancellationToken ct = default)
    {
        var thumbnailPath = GetThumbnailPath(sourcePath, thumbnailDirectory);

        if (File.Exists(thumbnailPath) && orientation == ImageOrientation.Normal)
            return thumbnailPath;

        Directory.CreateDirectory(thumbnailDirectory);

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var isImage = ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".tiff" or ".tif"
            or ".cr2" or ".nef" or ".arw" or ".dng" or ".gif" or ".bmp";

        if (isImage)
        {
            using var image = await Image.LoadAsync(sourcePath, ct);
            _adjustment.ApplyOrientation(image, orientation);
            var (w, h) = (image.Width, image.Height);

            if (w > h)
            {
                var ratio = 256.0 / w;
                image.Mutate(x => x.Resize(256, (int)(h * ratio)));
            }
            else
            {
                var ratio = 256.0 / h;
                image.Mutate(x => x.Resize((int)(w * ratio), 256));
            }

            var encoder = new JpegEncoder { Quality = 85 };
            await image.SaveAsync(thumbnailPath, encoder, ct);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Use Windows Shell API for video (title frame) and document (icon/thumbnail)
            await GenerateWindowsThumbnailAsync(sourcePath, thumbnailPath, ct);
        }
        else
        {
            // Fallback: create a blank placeholder for unsupported platforms
            using var blank = new Image<Rgba32>(256, 256, new Rgba32(240, 240, 240));
            await blank.SaveAsync(thumbnailPath, new JpegEncoder { Quality = 85 }, ct);
        }

        return thumbnailPath;
    }

    private static async Task GenerateWindowsThumbnailAsync(string sourcePath, string thumbnailPath, CancellationToken ct)
    {
        var shellItemFactoryGuid = new Guid("bcc18b79-ba16-442b-8bc4-c0d786f1503f");
        SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, shellItemFactoryGuid, out var imageFactory);

        if (imageFactory == null)
            return;

        try
        {
            var hr = imageFactory.GetImage(256, 256, SIIGBF_RESIZETOFIT | SIIGBF_THUMBNAILONLY, out var hBitmap);
            if (hr != 0)
            {
                // Try again without THUMBNAILONLY flag for icons
                hr = imageFactory.GetImage(256, 256, SIIGBF_RESIZETOFIT, out hBitmap);
                if (hr != 0)
                    return;
            }

            try
            {
                await SaveHBitmapToJpegAsync(hBitmap, thumbnailPath, ct);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                Marshal.ReleaseComObject(imageFactory);
        }
    }

    private static async Task SaveHBitmapToJpegAsync(IntPtr hBitmap, string path, CancellationToken ct)
    {
        var bmp = GetBitmapFromHBitmap(hBitmap);
        if (bmp == null) return;

        await bmp.SaveAsync(path, new JpegEncoder { Quality = 85 }, ct);
    }

    private static Image<Rgba32>? GetBitmapFromHBitmap(IntPtr hBitmap)
    {
        const uint BI_RGB = 0;
        const uint DIB_RGB_COLORS = 0;

        var bmpInfo = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out bmpInfo) == 0)
            return null;

        var width = bmpInfo.bmWidth;
        var height = bmpInfo.bmHeight;
        var stride = bmpInfo.bmWidthBytes;

        var pixelData = new byte[height * stride];
        var hdc = GetDC(IntPtr.Zero);
        var memDC = CreateCompatibleDC(hdc);
        var oldBmp = SelectObject(memDC, hBitmap);

        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
            biSizeImage = (uint)(width * height * 4)
        };

        GetDIBits(memDC, hBitmap, 0, (uint)height, pixelData, ref bmi, DIB_RGB_COLORS);

        SelectObject(memDC, oldBmp);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, hdc);

        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                var b = pixelData[offset];
                var g = pixelData[offset + 1];
                var r = pixelData[offset + 2];
                var a = pixelData[offset + 3];
                image[x, height - 1 - y] = new Rgba32(r, g, b, a);
            }
        }

        return image;
    }

    public string GetThumbnailPath(string sourcePath, string thumbnailDirectory)
    {
        var normalized = sourcePath.Replace('\\', '/');
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized))
        )[..16];
        var result = Path.Combine(thumbnailDirectory, $"{hash}.jpg");
        System.Diagnostics.Debug.WriteLine($"[adam] GetThumbnailPath: source={sourcePath} -> normalized={normalized} -> hash={hash} -> result={result}");
        return result;
    }

    // Windows Shell API for thumbnail extraction
    private const uint SIIGBF_RESIZETOFIT = 0x00000001;
    private const uint SIIGBF_THUMBNAILONLY = 0x00000020;

    [ComImport, Guid("bcc18b79-ba16-442b-8bc4-c0d786f1503f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(int cx, int cy, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cb, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}
