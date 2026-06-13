using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Converters;

/// <summary>
/// Tests for the OneNoteXmlToMarkdownConverter - the core conversion engine.
/// </summary>
public class OneNoteXmlToMarkdownConverterTests
{
    private readonly OneNoteXmlToMarkdownConverter _converter;

    public OneNoteXmlToMarkdownConverterTests()
    {
        _converter = new OneNoteXmlToMarkdownConverter();
    }

    #region Basic Conversion Tests

    [Fact]
    public void Convert_SimpleText_ReturnsMarkdown()
    {
        // Arrange
        var xml = CreatePageXml("<one:T><![CDATA[Hello World]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Hello World");
    }

    [Fact]
    public void Convert_PageWithTitle_IncludesH1Heading()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page Title]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Content]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("# My Page Title");
    }

    [Fact]
    public void Convert_EmptyPage_ReturnsNonNull()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Text Formatting Tests

    [Fact]
    public void Convert_BoldText_ConvertsToStrong()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-weight:bold'>Bold Text</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("**Bold Text**");
    }

    [Fact]
    public void Convert_ItalicText_ConvertsToEmphasis()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-style:italic'>Italic Text</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("*Italic Text*");
    }

    [Fact]
    public void Convert_StrikethroughText_ConvertsToDelTag()
    {
        // Arrange - Strikethrough uses T element style attribute, not span
        var xml = CreatePageXmlWithStyle("Deleted", "text-decoration:line-through");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("~~Deleted~~");
    }

    [Fact]
    public void Convert_YellowHighlight_ConvertsToObsidianHighlight()
    {
        // Arrange - default (yellow) highlight
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:yellow'>Highlighted</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("==Highlighted==");
    }

    [Fact]
    public void Convert_YellowHexHighlight_ConvertsToObsidianHighlight()
    {
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:#FFFF00'>Hi</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("==Hi==");
    }

    [Fact]
    public void Convert_NonYellowHighlight_ConvertsToMarkHtmlWithColor()
    {
        // Arrange - a green highlight should keep its color via <mark>
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:#00FF00'>Green</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("<mark style=\"background:#00ff00\">Green</mark>");
    }

    [Fact]
    public void Convert_HighlightWithBoldInside_NestsCorrectly()
    {
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:yellow'><span style='font-weight:bold'>Both</span></span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("==**Both**==");
    }

    [Fact]
    public void Convert_Underline_ConvertsToUTag()
    {
        var xml = CreatePageXml("<one:T><![CDATA[<span style='text-decoration:underline'>Under</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("<u>Under</u>");
    }

    [Fact]
    public void Convert_Superscript_ConvertsToSupTag()
    {
        var xml = CreatePageXml("<one:T><![CDATA[E=mc<span style='vertical-align:super'>2</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("<sup>2</sup>");
    }

    [Fact]
    public void Convert_Subscript_ConvertsToSubTag()
    {
        var xml = CreatePageXml("<one:T><![CDATA[H<span style='vertical-align:sub'>2</span>O]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("<sub>2</sub>");
    }

    [Fact]
    public void Convert_FontColor_DroppedByDefault()
    {
        var xml = CreatePageXml("<one:T><![CDATA[<span style='color:#FF0000'>Red</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("Red");
        result.Should().NotContain("color:");
    }

    [Fact]
    public void Convert_FontColor_PreservedWhenEnabled()
    {
        var converter = new OneNoteXmlToMarkdownConverter { IncludeFontColors = true };
        var xml = CreatePageXml("<one:T><![CDATA[<span style='color:#FF0000'>Red</span>]]></one:T>");

        var result = converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("<span style=\"color:#ff0000\">Red</span>");
    }

    [Fact]
    public void Convert_FontColorWithBackground_KeepsHighlightButNotColorWhenDisabled()
    {
        // Foreground color disabled, but the yellow highlight should still apply.
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:yellow;color:#FF0000'>Mix</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("==Mix==");
        result.Should().NotContain("color:");
    }

    [Fact]
    public void Convert_BoldAndItalic_PreservesBothFormats()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-weight:bold;font-style:italic'>Bold Italic</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        // Should contain both bold and italic markers
        result.Should().Contain("**");
        result.Should().Contain("*");
    }

    #endregion

    #region List Tests

    [Fact]
    public void Convert_BulletList_CreatesUnorderedList()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Item 1]]></one:T>
                        </one:OE>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Item 2]]></one:T>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("- Item 1");
        result.Should().Contain("- Item 2");
    }

    [Fact]
    public void Convert_NumberedList_CreatesOrderedList()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Number /></one:List>
                            <one:T><![CDATA[First]]></one:T>
                        </one:OE>
                        <one:OE>
                            <one:List><one:Number /></one:List>
                            <one:T><![CDATA[Second]]></one:T>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("1. First");
        result.Should().Contain("2. Second");
    }

    [Fact]
    public void Convert_NestedBulletList_PreservesIndentation()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Parent]]></one:T>
                            <one:OEChildren>
                                <one:OE>
                                    <one:List><one:Bullet /></one:List>
                                    <one:T><![CDATA[Child]]></one:T>
                                </one:OE>
                            </one:OEChildren>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("- Parent");
        result.Should().Contain("Child"); // Should be indented or nested
    }

    #endregion

    #region Link Tests

    [Fact]
    public void Convert_SimpleLink_CreatesMarkdownLink()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com"">Click Here</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("[Click Here](https://example.com)");
    }

    [Fact]
    public void Convert_NakedUrl_ContainsLink()
    {
        // Arrange - when link text matches URL
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com"">https://example.com</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert - URL should be preserved in some link format
        result.Should().Contain("https://example.com");
    }

    [Fact]
    public void Convert_LinkWithSpecialChars_PreservesUrl()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com/path?query=value&other=123"">Link</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("https://example.com/path?query=value&other=123");
    }

    #endregion

    #region Table Tests

    [Fact]
    public void Convert_SimpleTable_CreatesMarkdownTable()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:Table>
                                <one:Row>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[A]]></one:T></one:OE></one:OEChildren></one:Cell>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[B]]></one:T></one:OE></one:OEChildren></one:Cell>
                                </one:Row>
                                <one:Row>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[1]]></one:T></one:OE></one:OEChildren></one:Cell>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[2]]></one:T></one:OE></one:OEChildren></one:Cell>
                                </one:Row>
                            </one:Table>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert - ReverseMarkdown converts tables to Markdown format
        result.Should().Contain("|");
        result.Should().Contain("---");
    }

    #endregion

    #region Image Tests

    [Fact]
    public void Convert_ImageWithCustomRelativeAssetsPath_UsesRelativeAssetsPathInMarkdown()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "shared", "assets");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            // Act
            var result = _converter.Convert(xml, assetsFolder, "../shared/assets", null, "page");

            // Assert
            result.Should().Contain("../shared/assets/page_image_0001.png");
            File.Exists(Path.Combine(assetsFolder, "page_image_0001.png")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_ImageWithExistingAssetFile_OverwritesAssetFile()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var existingAssetPath = Path.Combine(assetsFolder, "page_image_0001.png");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            Directory.CreateDirectory(assetsFolder);
            File.WriteAllBytes(existingAssetPath, new byte[] { 9, 9, 9 });

            // Act
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            // Assert
            result.Should().Contain("assets/page_image_0001.png");
            File.ReadAllBytes(existingAssetPath).Should().Equal(new byte[] { 1, 2, 3 });
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    #endregion

    #region Attachment Tests

    [Fact]
    public void Convert_InsertedFileWithCachedBinary_CopiesFileAndEmitsLink()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var cacheFile = Path.Combine(tempRoot, "OneNoteOfflineCache_Files", "abc123.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllBytes(cacheFile, new byte[] { 1, 2, 3, 4 });

        var xml = CreatePageXml(
            $@"<one:InsertedFile pathCache=""{cacheFile.Replace("\\", "\\\\")}"" pathSource=""C:\\docs\\Report.pdf"" preferredName=""Report.pdf"" />");

        try
        {
            // Act
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            // Assert
            result.Should().Contain("Report.pdf");
            result.Should().Contain("](assets/");
            result.Should().Contain(".pdf)");
            // The cached binary was copied into the assets folder.
            Directory.GetFiles(assetsFolder, "*.pdf").Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_InsertedFileWithBinCache_UsesRealExtensionFromPreferredName()
    {
        // The cache file is OneNote's ".bin" (no real extension), but preferredName has ".pdf".
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var cacheFile = Path.Combine(tempRoot, "cache", "00000BN7.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllBytes(cacheFile, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        var xml = CreatePageXml(
            $@"<one:InsertedFile pathCache=""{cacheFile.Replace("\\", "\\\\")}"" pathSource=""C:\\Users\\x\\Downloads\\1711.05101.pdf"" preferredName=""1711.05101.pdf"" />");

        try
        {
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            result.Should().Contain(".pdf)");
            result.Should().NotContain(".bin");
            Directory.GetFiles(assetsFolder, "*.pdf").Should().ContainSingle();
            Directory.GetFiles(assetsFolder, "*.bin").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_InsertedFileWithNoNameExtension_SniffsContentType()
    {
        // No usable extension in the names; the cached file content is a PDF.
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var cacheFile = Path.Combine(tempRoot, "cache", "00000BN7.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllBytes(cacheFile, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 fake pdf body"));

        var xml = CreatePageXml(
            $@"<one:InsertedFile pathCache=""{cacheFile.Replace("\\", "\\\\")}"" preferredName=""scan_2024"" />");

        try
        {
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            Directory.GetFiles(assetsFolder, "*.pdf").Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_InsertedFileWithoutCachePath_EmitsUnavailablePlaceholder()
    {
        // Arrange - no pathCache attribute at all
        var xml = CreatePageXml(@"<one:InsertedFile pathSource=""C:\\docs\\Report.pdf"" preferredName=""Report.pdf"" />");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "page");

        // Assert
        result.Should().Contain("Attachment unavailable");
        result.Should().Contain("Report.pdf");
    }

    [Fact]
    public void Convert_InsertedFileWithMissingCacheFile_EmitsNotDownloadedPlaceholder()
    {
        // Arrange - pathCache points to a file that does not exist.
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.pdf");
        var xml = CreatePageXml(
            $@"<one:InsertedFile pathCache=""{missingPath.Replace("\\", "\\\\")}"" preferredName=""Slides.pptx"" />");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "page");

        // Assert
        result.Should().Contain("Attachment unavailable");
        result.Should().Contain("Slides.pptx");
        result.Should().Contain("not downloaded locally");
    }

    #endregion

    #region Heading and Task Tests

    private static string CreatePageXmlWithDefsAndOes(string defs, string oes)
    {
        return $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                {defs}
                <one:Outline>
                    <one:OEChildren>
                        {oes}
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
    }

    [Fact]
    public void Convert_QuickStyleHeading_ConvertsToMarkdownHeading()
    {
        var xml = CreatePageXmlWithDefsAndOes(
            @"<one:QuickStyleDef index=""1"" name=""h1""/><one:QuickStyleDef index=""2"" name=""h2""/><one:QuickStyleDef index=""3"" name=""p""/>",
            @"<one:OE quickStyleIndex=""1""><one:T><![CDATA[Big Heading]]></one:T></one:OE>
              <one:OE quickStyleIndex=""2""><one:T><![CDATA[Sub Heading]]></one:T></one:OE>
              <one:OE quickStyleIndex=""3""><one:T><![CDATA[Body text]]></one:T></one:OE>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("# Big Heading");
        result.Should().Contain("## Sub Heading");
        result.Should().Contain("Body text");
    }

    [Fact]
    public void Convert_ToDoTags_ConvertToTaskList()
    {
        var xml = CreatePageXmlWithDefsAndOes(
            @"<one:TagDef index=""0"" type=""0"" symbol=""3"" name=""To Do""/>",
            @"<one:OE><one:Tag index=""0"" completed=""false""/><one:T><![CDATA[Unchecked task]]></one:T></one:OE>
              <one:OE><one:Tag index=""0"" completed=""true""/><one:T><![CDATA[Done task]]></one:T></one:OE>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("- [ ] Unchecked task");
        result.Should().Contain("- [x] Done task");
    }

    [Fact]
    public void Convert_NonTodoTag_DoesNotBecomeCheckbox()
    {
        // symbol != 3 is not a To Do tag (e.g. "Important" star)
        var xml = CreatePageXmlWithDefsAndOes(
            @"<one:TagDef index=""0"" type=""0"" symbol=""5"" name=""Important""/>",
            @"<one:OE><one:Tag index=""0"" completed=""false""/><one:T><![CDATA[Starred item]]></one:T></one:OE>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("Starred item");
        result.Should().NotContain("- [ ]");
    }

    #endregion

    #region Table and Image Size Tests

    [Fact]
    public void Convert_MultiLineTableCell_KeepsRowIntactWithBr()
    {
        var xml = $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline><one:OEChildren><one:OE>
                    <one:Table>
                        <one:Row>
                            <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[Header A]]></one:T></one:OE></one:OEChildren></one:Cell>
                            <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[Header B]]></one:T></one:OE></one:OEChildren></one:Cell>
                        </one:Row>
                        <one:Row>
                            <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[Org]]></one:T></one:OE></one:OEChildren></one:Cell>
                            <one:Cell><one:OEChildren>
                                <one:OE><one:T><![CDATA[Microsoft Research;]]></one:T></one:OE>
                                <one:OE><one:T><![CDATA[KAIST;]]></one:T></one:OE>
                                <one:OE><one:T><![CDATA[Seoul National University]]></one:T></one:OE>
                            </one:OEChildren></one:Cell>
                        </one:Row>
                    </one:Table>
                </one:OE></one:OEChildren></one:Outline>
            </one:Page>";

        var result = _converter.Convert(xml, "", "assets", null, "test");

        // The multi-line cell stays on one table row, joined with <br>.
        result.Should().Contain("Microsoft Research;<br>KAIST;<br>Seoul National University");
        // The cell content must not be split across physical lines (which would break the table).
        result.Should().NotContain("Microsoft Research;\nKAIST;");
    }

    [Fact]
    public void Convert_ImageWithSize_EmitsObsidianWidth()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Size width=""441.0"" height=""112.8"" isSetByUser=""true""/><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            result.Should().Contain("![image|441](assets/page_image_0001.png)");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_ImageWithoutSize_HasNoWidth()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            result.Should().Contain("![image](assets/page_image_0001.png)");
            result.Should().NotContain("|");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    #endregion

    #region Math Tests

    private const string MathNsAttr = @"xmlns=""http://www.w3.org/1998/Math/MathML""";

    [Fact]
    public void Convert_InlineMathML_ConvertsToInlineLatex()
    {
        var xml = CreatePageXml(
            $@"<one:T><![CDATA[<!--[if mathML]><math {MathNsAttr}><mi>x</mi></math><![endif]--><span>: input;</span>]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("$x$");
        result.Should().Contain(": input;");
        result.Should().NotContain("mathML");
        result.Should().NotContain("<math");
    }

    [Fact]
    public void Convert_SubscriptMathML_ConvertsToLatexSubscript()
    {
        var xml = CreatePageXml(
            $@"<one:T><![CDATA[<!--[if mathML]><math {MathNsAttr}><msub><mi>s</mi><mrow><mi>t</mi><mi>h</mi></mrow></msub></math><![endif]-->]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        // xsltml wraps the script base in braces; equivalent LaTeX to s_{th}.
        result.Should().Contain("${s}_{th}$");
    }

    [Fact]
    public void Convert_FractionMathML_ConvertsToLatexFrac()
    {
        var xml = CreatePageXml(
            $@"<one:T><![CDATA[<!--[if mathML]><math {MathNsAttr}><mfrac><mi>a</mi><mi>b</mi></mfrac></math><![endif]-->]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain(@"$\frac{a}{b}$");
    }

    [Fact]
    public void Convert_BlockMathML_ConvertsToDisplayLatex()
    {
        var xml = CreatePageXml(
            $@"<one:T><![CDATA[<!--[if mathML]><math {MathNsAttr} display=""block""><mi>H</mi><mo>−</mo><mi>c</mi></math><![endif]-->]]></one:T>");

        var result = _converter.Convert(xml, "", "assets", null, "test");

        result.Should().Contain("$$H-c$$");
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public void Convert_MultipleBlankLines_ReducedToTwo()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Line 1]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[Line 2]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        // Should not have more than 2 consecutive newlines (3+ newline chars in a row)
        result.Should().NotContain("\n\n\n\n");
    }

    [Fact]
    public void Convert_HtmlEntities_Decoded()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Tom &amp; Jerry]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Tom & Jerry");
    }

    [Fact]
    public void Convert_UnicodeContent_Preserved()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Hello 世界 🎉]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Hello 世界 🎉");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Convert_NullBinaryFetcher_HandlesGracefully()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Simple text]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Simple text");
    }

    [Fact]
    public void Convert_EmptyPrefix_HandlesGracefully()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Content]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Convert_SpecialCharsInPrefix_Sanitized()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Content]]></one:T>");

        // Act - prefix with invalid filename characters
        var result = _converter.Convert(xml, "", "assets", null, "test:page/name");

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal OneNote page XML with the given content element.
    /// </summary>
    private static string CreatePageXml(string contentElement)
    {
        return $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>{contentElement}</one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
    }

    /// <summary>
    /// Creates a OneNote page XML with text that has a style attribute on the T element.
    /// </summary>
    private static string CreatePageXmlWithStyle(string text, string style)
    {
        return $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T style=""{style}""><![CDATA[{text}]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
    }

    #endregion
}
