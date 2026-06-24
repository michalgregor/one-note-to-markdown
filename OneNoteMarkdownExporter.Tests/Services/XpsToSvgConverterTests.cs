using System.IO;
using System.IO.Compression;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class XpsToSvgConverterTests
{
    [Fact]
    public void ConvertToSvgFiles_ConvertsXpsPathsAndArgbColorsToSvg()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var xpsPath = Path.Combine(tempRoot, "page.xps");
        var outputFolder = Path.Combine(tempRoot, "assets");

        try
        {
            Directory.CreateDirectory(tempRoot);
            CreateMinimalXps(xpsPath);

            var svgPaths = XpsToSvgConverter.ConvertToSvgFiles(xpsPath, outputFolder, "Ink Page");

            svgPaths.Should().ContainSingle();
            var svg = File.ReadAllText(svgPaths[0]);
            svg.Should().Contain("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="50" viewBox="0 0 100 50">""");
            svg.Should().Contain("""<path d="M 1,2 L 3,4 Z" fill="#004F8B"/>""");
            svg.Should().Contain("""<text x="10" y="20" font-size="12" font-family="Segoe UI, Arial, sans-serif" fill="#767676" fill-opacity="0.502">Title</text>""");
            svg.Should().NotContain("#FF004F8B");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void CreateMinimalXps(string xpsPath)
    {
        const string fixedPage = """
            <FixedPage xmlns="http://schemas.microsoft.com/xps/2005/06" Width="100" Height="50">
              <Path Data="F 1 M 1,2 L 3,4 Z">
                <Path.Fill>
                  <SolidColorBrush Color="#FF004F8B" />
                </Path.Fill>
              </Path>
              <Glyphs OriginX="10" OriginY="20" FontRenderingEmSize="12" Fill="#80767676" UnicodeString="Title" />
            </FixedPage>
            """;

        using var archive = ZipFile.Open(xpsPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("Documents/1/Pages/1.fpage");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(fixedPage);
    }
}
