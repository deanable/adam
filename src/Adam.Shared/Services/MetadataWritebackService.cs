using System.Text;
using System.Xml.Linq;
using Adam.Shared.Models;

namespace Adam.Shared.Services;

public class MetadataWritebackService
{
    public async Task WriteMetadataAsync(string filePath, MetadataProfile profile, CancellationToken ct = default)
    {
        var xmp = BuildXmpPacket(profile);
        var xmpBytes = Encoding.UTF8.GetBytes(xmp);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                await EmbedInJpegAsync(filePath, xmpBytes, ct);
                break;
            case ".png":
                await EmbedInPngAsync(filePath, xmpBytes, ct);
                break;
            case ".webp":
                await EmbedInWebPAsync(filePath, xmpBytes, ct);
                break;
            case ".tiff":
            case ".tif":
                await EmbedInTiffAsync(filePath, xmpBytes, ct);
                break;
        }
    }

    public async Task WriteSidecarXmpAsync(string rawFilePath, MetadataProfile profile, CancellationToken ct = default)
    {
        var xmp = BuildXmpPacket(profile);
        var sidecarPath = Path.ChangeExtension(rawFilePath, ".xmp");
        await File.WriteAllTextAsync(sidecarPath, xmp, Encoding.UTF8, ct);
    }

    public bool SupportsEmbeddedMetadata(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".tiff" or ".tif" or ".png" or ".webp";
    }

    public bool IsRawFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".cr2" or ".nef" or ".arw" or ".dng" or ".raf" or ".orf" or ".pef" or ".rw2";
    }

    /// <summary>
    /// Write metadata from a DigitalAsset (including new Phase 4 fields: Rating, Label, Flag, GPS, Copyright).
    /// </summary>
    /// <summary>
    /// Thrown when the target file is read-only.
    /// </summary>
    public class ReadOnlyFileException : IOException
    {
        public ReadOnlyFileException(string path)
            : base($"File is read-only: {path}") { }
    }

    public async Task WriteMetadataAsync(string filePath, DigitalAsset asset, CancellationToken ct = default)
    {
        if (File.Exists(filePath) && File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly))
            throw new ReadOnlyFileException(filePath);

        var xmp = BuildXmpPacket(asset);
        var xmpBytes = Encoding.UTF8.GetBytes(xmp);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                await EmbedInJpegAsync(filePath, xmpBytes, ct);
                break;
            case ".png":
                await EmbedInPngAsync(filePath, xmpBytes, ct);
                break;
            case ".webp":
                await EmbedInWebPAsync(filePath, xmpBytes, ct);
                break;
            case ".tiff":
            case ".tif":
                await EmbedInTiffAsync(filePath, xmpBytes, ct);
                break;
        }
    }

    /// <summary>
    /// Write XMP sidecar for RAW files from a DigitalAsset.
    /// </summary>
    public async Task WriteSidecarXmpAsync(string rawFilePath, DigitalAsset asset, CancellationToken ct = default)
    {
        var sidecarPath = Path.ChangeExtension(rawFilePath, ".xmp");
        if (File.Exists(sidecarPath) && File.GetAttributes(sidecarPath).HasFlag(FileAttributes.ReadOnly))
            throw new ReadOnlyFileException(sidecarPath);

        var xmp = BuildXmpPacket(asset);
        await File.WriteAllTextAsync(sidecarPath, xmp, Encoding.UTF8, ct);
    }

    private static string BuildXmpPacket(MetadataProfile profile)
    {
        var rdf = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var xmp = XNamespace.Get("http://ns.adobe.com/xap/1.0/");
        var photoshop = XNamespace.Get("http://ns.adobe.com/photoshop/1.0/");
        var rights = XNamespace.Get("http://ns.adobe.com/xap/1.0/rights/");
        var iptc = XNamespace.Get("http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/");
        var xNs = XNamespace.Get("adobe:ns:meta/");

        var description = new XElement(rdf + "Description",
            new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmp", xmp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "photoshop", photoshop.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "rights", rights.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "Iptc4xmpCore", iptc.NamespaceName)
        );

        if (!string.IsNullOrEmpty(profile.Creator))
        {
            description.Add(new XElement(dc + "creator",
                new XElement(rdf + "Seq",
                    new XElement(rdf + "li", profile.Creator)
                )
            ));
        }

        if (!string.IsNullOrEmpty(profile.Title))
        {
            description.Add(new XElement(dc + "title",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        profile.Title
                    )
                )
            ));
        }

        if (!string.IsNullOrEmpty(profile.Description))
        {
            description.Add(new XElement(dc + "description",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        profile.Description
                    )
                )
            ));
        }

        if (!string.IsNullOrEmpty(profile.Copyright))
        {
            description.Add(new XElement(dc + "rights",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        profile.Copyright
                    )
                )
            ));
        }

        if (!string.IsNullOrEmpty(profile.Headline))
            description.Add(new XElement(photoshop + "Headline", profile.Headline));

        if (profile.Rating.HasValue)
            description.Add(new XAttribute(xmp + "Rating", profile.Rating.Value));

        if (!string.IsNullOrEmpty(profile.UsageTerms))
        {
            description.Add(new XElement(rights + "UsageTerms",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        profile.UsageTerms
                    )
                )
            ));
        }

        var location = BuildLocationString(profile.City, profile.State, profile.Country);
        if (location != null)
            description.Add(new XElement(iptc + "Location", location));

        var xmpMeta = new XElement(xNs + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", xNs.NamespaceName),
            new XElement(rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", rdf.NamespaceName),
                description
            )
        );

        var innerXml = xmpMeta.ToString(SaveOptions.DisableFormatting);
        return $"<?xpacket begin=\"\ufeff\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n{innerXml}\n<?xpacket end=\"w\"?>";
    }

    /// <summary>
    /// Build XMP packet from DigitalAsset (Phase 4 fields: Rating, Label, Flag, GPS, Copyright).
    /// </summary>
    private static string BuildXmpPacket(DigitalAsset asset)
    {
        var rdf = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var dc = XNamespace.Get("http://purl.org/dc/elements/1.1/");
        var xmp = XNamespace.Get("http://ns.adobe.com/xap/1.0/");
        var photoshop = XNamespace.Get("http://ns.adobe.com/photoshop/1.0/");
        var rights = XNamespace.Get("http://ns.adobe.com/xap/1.0/rights/");
        var iptc = XNamespace.Get("http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/");
        var xNs = XNamespace.Get("adobe:ns:meta/");

        var description = new XElement(rdf + "Description",
            new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmp", xmp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "photoshop", photoshop.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "rights", rights.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "Iptc4xmpCore", iptc.NamespaceName)
        );

        if (!string.IsNullOrEmpty(asset.Title))
        {
            description.Add(new XElement(dc + "title",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        asset.Title
                    )
                )
            ));
        }

        if (!string.IsNullOrEmpty(asset.Description))
        {
            description.Add(new XElement(dc + "description",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        asset.Description
                    )
                )
            ));
        }

        if (!string.IsNullOrEmpty(asset.Copyright))
        {
            description.Add(new XElement(dc + "rights",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        asset.Copyright
                    )
                )
            ));
        }

        // Keywords as dc:subject
        if (asset.Keywords.Count > 0)
        {
            var keywordBag = new XElement(rdf + "Bag");
            foreach (var kw in asset.Keywords)
                keywordBag.Add(new XElement(rdf + "li", kw.Name));
            description.Add(new XElement(dc + "subject", keywordBag));
        }

        if (asset.Rating > 0)
            description.Add(new XAttribute(xmp + "Rating", asset.Rating));

        if (asset.Label != AssetLabel.None)
            description.Add(new XAttribute(photoshop + "Label", asset.Label.ToString()));

        // GPS coordinates
        if (asset.GpsLatitude.HasValue && asset.GpsLongitude.HasValue)
        {
            var exifNs = XNamespace.Get("http://ns.adobe.com/exif/1.0/");
            description.Add(new XAttribute(XNamespace.Xmlns + "exif", exifNs.NamespaceName));
            description.Add(new XElement(exifNs + "GPSLatitude", asset.GpsLatitude.Value.ToString("F6")));
            description.Add(new XElement(exifNs + "GPSLongitude", asset.GpsLongitude.Value.ToString("F6")));
        }

        var xmpMeta = new XElement(xNs + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", xNs.NamespaceName),
            new XElement(rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", rdf.NamespaceName),
                description
            )
        );

        var innerXml = xmpMeta.ToString(SaveOptions.DisableFormatting);
        return $"<?xpacket begin=\"\ufeff\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n{innerXml}\n<?xpacket end=\"w\"?>";
    }

    private static string? BuildLocationString(string? city, string? state, string? country)
    {
        var parts = new[] { city, state, country };
        var nonEmpty = parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return nonEmpty.Length > 0 ? string.Join(", ", nonEmpty) : null;
    }

    private static async Task EmbedInJpegAsync(string filePath, byte[] xmpBytes, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(filePath, ct);
        var app1 = BuildJpegApp1Marker(xmpBytes);
        var existingOffset = FindExistingXmpApp1(data);

        byte[] result;
        if (existingOffset >= 0)
        {
            var oldLen = ((data[existingOffset + 2] << 8) | data[existingOffset + 3]) + 2;
            result = ReplaceSegment(data, existingOffset, oldLen, app1);
        }
        else
        {
            result = InsertAfter(data, 2, app1);
        }

        await File.WriteAllBytesAsync(filePath, result, ct);
    }

    private static byte[] BuildJpegApp1Marker(byte[] xmpBytes)
    {
        var id = "http://ns.adobe.com/xap/1.0/" + '\0';
        var idBytes = Encoding.ASCII.GetBytes(id);
        var payloadLen = idBytes.Length + xmpBytes.Length;
        var marker = new byte[4 + payloadLen];
        marker[0] = 0xFF;
        marker[1] = 0xE1;
        marker[2] = (byte)((payloadLen + 2) >> 8);
        marker[3] = (byte)(payloadLen + 2);
        idBytes.CopyTo(marker, 4);
        xmpBytes.CopyTo(marker, 4 + idBytes.Length);
        return marker;
    }

    private static int FindExistingXmpApp1(byte[] jpeg)
    {
        var id = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0\0");
        var offset = 2;
        while (offset + 4 <= jpeg.Length)
        {
            if (jpeg[offset] != 0xFF) break;
            var marker = jpeg[offset + 1];
            if (marker == 0xDA || marker == 0xD9) break;
            if (marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }
            var len = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
            if (marker == 0xE1 && len >= id.Length + 2)
            {
                if (BytesMatch(jpeg, offset + 4, id))
                    return offset;
            }
            offset += 2 + len;
        }
        return -1;
    }

    private static bool BytesMatch(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i])
                return false;
        return true;
    }

    private static byte[] ReplaceSegment(byte[] data, int offset, int oldLen, byte[] newSegment)
    {
        var result = new byte[data.Length - oldLen + newSegment.Length];
        var afterOld = offset + oldLen;
        Array.Copy(data, 0, result, 0, offset);
        newSegment.CopyTo(result, offset);
        Array.Copy(data, afterOld, result, offset + newSegment.Length, data.Length - afterOld);
        return result;
    }

    private static byte[] InsertAfter(byte[] data, int offset, byte[] segment)
    {
        var result = new byte[data.Length + segment.Length];
        Array.Copy(data, 0, result, 0, offset);
        segment.CopyTo(result, offset);
        Array.Copy(data, offset, result, offset + segment.Length, data.Length - offset);
        return result;
    }

    private static async Task EmbedInPngAsync(string filePath, byte[] xmpBytes, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(filePath, ct);
        var firstChunkLen = ReadBigEndian32(data, 8);
        var insertOffset = 8 + 4 + 4 + (int)firstChunkLen + 4;
        var textChunk = BuildPngTextChunk(xmpBytes);
        var result = InsertAfter(data, insertOffset, textChunk);
        await File.WriteAllBytesAsync(filePath, result, ct);
    }

    private static byte[] BuildPngTextChunk(byte[] xmpBytes)
    {
        var keyword = "XML:com.adobe.xmp\0";
        var keywordBytes = Encoding.ASCII.GetBytes(keyword);
        var chunkData = new byte[keywordBytes.Length + xmpBytes.Length];
        keywordBytes.CopyTo(chunkData, 0);
        xmpBytes.CopyTo(chunkData, keywordBytes.Length);

        var length = chunkData.Length;
        var chunk = new byte[4 + 4 + chunkData.Length + 4];
        WriteBigEndian32(chunk, 0, (uint)length);
        Encoding.ASCII.GetBytes("tEXt", 0, 4, chunk, 4);
        chunkData.CopyTo(chunk, 8);

        var crc = PngCrc(chunk, 4, 4 + chunkData.Length);
        WriteBigEndian32(chunk, 8 + chunkData.Length, crc);
        return chunk;
    }

    private static uint PngCrc(byte[] data, int offset, int length)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                if ((c & 1) == 1)
                    c = 0xedb88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[n] = c;
        }

        var crc = 0xffffffffu;
        for (var i = 0; i < length; i++)
            crc = table[(crc ^ data[offset + i]) & 0xff] ^ (crc >> 8);
        return crc ^ 0xffffffff;
    }

    private static async Task EmbedInWebPAsync(string filePath, byte[] xmpBytes, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(filePath, ct);
        var insertOffset = 12;
        if (data.Length > 12)
        {
            var firstChunkLen = ReadLittleEndian32(data, 16);
            insertOffset = 12 + 4 + 4 + (int)firstChunkLen;
        }

        var xmpChunk = BuildWebPXmpChunk(xmpBytes);
        var result = InsertAfter(data, insertOffset, xmpChunk);
        WriteLittleEndian32(result, 4, (uint)(result.Length - 8));
        await File.WriteAllBytesAsync(filePath, result, ct);
    }

    private static byte[] BuildWebPXmpChunk(byte[] xmpBytes)
    {
        var chunk = new byte[4 + 4 + xmpBytes.Length];
        Encoding.ASCII.GetBytes("XMP ", 0, 4, chunk, 0);
        WriteLittleEndian32(chunk, 4, (uint)xmpBytes.Length);
        xmpBytes.CopyTo(chunk, 8);
        return chunk;
    }

    private static async Task EmbedInTiffAsync(string filePath, byte[] xmpBytes, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(filePath, ct);
        var result = new byte[data.Length + xmpBytes.Length];
        data.CopyTo(result, 0);
        xmpBytes.CopyTo(result, data.Length);
        await File.WriteAllBytesAsync(filePath, result, ct);
    }

    private static uint ReadBigEndian32(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    private static uint ReadLittleEndian32(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private static void WriteBigEndian32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static void WriteLittleEndian32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
