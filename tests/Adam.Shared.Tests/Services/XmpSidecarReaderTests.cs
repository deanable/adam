using System.Text;
using Adam.Shared.Services;

namespace Adam.Shared.Tests.Services;

public sealed class XmpSidecarReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly XmpSidecarReader _reader = new();

    public XmpSidecarReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteXmp(string content)
    {
        var path = Path.Combine(_tempDir, "test.xmp");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static string MakePacket(string innerXml)
        => $@"<?xpacket begin=""﻿"" id=""W5M0MpCehiHzreSzNTczkc9d""?>
<x:xmpmeta xmlns:x=""adobe:ns:meta/"">
  <rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"">
    <rdf:Description rdf:about=""""
      xmlns:dc=""http://purl.org/dc/elements/1.1/""
      xmlns:xmp=""http://ns.adobe.com/xap/1.0/""
      xmlns:photoshop=""http://ns.adobe.com/photoshop/1.0/"">
      {innerXml}
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>
<?xpacket end=""w""?>";

    [Fact]
    public void ReadSidecar_NonExistentFile_ReturnsNull()
    {
        var result = _reader.ReadSidecar(Path.Combine(_tempDir, "nonexistent.xmp"));
        Assert.Null(result);
    }

    [Fact]
    public void ReadSidecar_TitleAndDescription_Extracted()
    {
        var xmp = MakePacket(@"
      <dc:title>
        <rdf:Alt>
          <rdf:li xml:lang=""x-default"">My Document Title</rdf:li>
        </rdf:Alt>
      </dc:title>
      <dc:description>
        <rdf:Alt>
          <rdf:li xml:lang=""x-default"">A detailed description of the document</rdf:li>
        </rdf:Alt>
      </dc:description>");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.Equal("My Document Title", result.Title);
        Assert.Equal("A detailed description of the document", result.Description);
    }

    [Fact]
    public void ReadSidecar_FlatKeywords_Extracted()
    {
        var xmp = MakePacket(@"
      <dc:subject>
        <rdf:Bag>
          <rdf:li>keyword1</rdf:li>
          <rdf:li>keyword2</rdf:li>
          <rdf:li>keyword3</rdf:li>
        </rdf:Bag>
      </dc:subject>");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.Contains("keyword1", result.Keywords);
        Assert.Contains("keyword2", result.Keywords);
        Assert.Contains("keyword3", result.Keywords);
        Assert.Equal(3, result.Keywords.Count);
    }

    [Fact]
    public void ReadSidecar_HierarchicalKeywords_TakePrecedence()
    {
        var xmp = MakePacket(@"
      <dc:subject>
        <rdf:Bag>
          <rdf:li>flatKeyword</rdf:li>
        </rdf:Bag>
      </dc:subject>
      <lr:HierarchicalSubject xmlns:lr=""http://ns.adobe.com/lightroom/1.0/"">
        Nature|Landscape|Sunset
      </lr:HierarchicalSubject>
      <lr:HierarchicalSubject xmlns:lr=""http://ns.adobe.com/lightroom/1.0/"">
        Travel|Europe|France
      </lr:HierarchicalSubject>");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.True(result.HasHierarchicalKeywords);
        Assert.Contains("Nature|Landscape|Sunset", result.Keywords);
        Assert.Contains("Travel|Europe|France", result.Keywords);
        Assert.Equal(2, result.Keywords.Count);
    }

    [Fact]
    public void ReadSidecar_Rating_Extracted()
    {
        // Use element format for rating (not attribute) since MakePacket
        // places innerXml as element content inside rdf:Description
        var xmp = MakePacket(@"<xmp:Rating>4</xmp:Rating>");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.Equal(4, result.Rating);
    }

    [Fact]
    public void ReadSidecar_PhotoshopHeadline_AsFallbackDescription()
    {
        var xmp = MakePacket(@"<photoshop:Headline>Headline as description</photoshop:Headline>");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.Equal("Headline as description", result.Description);
    }

    [Fact]
    public void ReadSidecar_EmptySidecar_ReturnsEmpty()
    {
        var xmp = MakePacket("");
        var path = WriteXmp(xmp);

        var result = _reader.ReadSidecar(path);

        Assert.NotNull(result);
        Assert.Null(result.Title);
        Assert.Null(result.Description);
        Assert.Empty(result.Keywords);
        Assert.Null(result.Rating);
    }

    [Fact]
    public void SidecarExists_ReturnsTrueForExistingFile()
    {
        var docPath = Path.Combine(_tempDir, "test.docx");
        var sidecarPath = XmpSidecarReader.GetSidecarPath(docPath);
        File.WriteAllText(sidecarPath, MakePacket(""), Encoding.UTF8);

        Assert.True(XmpSidecarReader.SidecarExists(docPath));
    }

    [Fact]
    public void SidecarExists_ReturnsFalseForMissingSidecar()
    {
        var docPath = Path.Combine(_tempDir, "test.docx");
        Assert.False(XmpSidecarReader.SidecarExists(docPath));
    }

    [Fact]
    public void GetSidecarPath_ReturnsCorrectPath()
    {
        var path = XmpSidecarReader.GetSidecarPath("/path/to/document.docx");
        Assert.Equal("/path/to/document.xmp", path);
    }
}
