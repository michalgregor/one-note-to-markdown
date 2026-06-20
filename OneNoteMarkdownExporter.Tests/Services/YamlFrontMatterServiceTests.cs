using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class YamlFrontMatterServiceTests
{
    [Fact]
    public void AddFrontMatter_WithFullMetadata_PrependsYamlBlock()
    {
        var service = new YamlFrontMatterService();
        var page = new OneNoteItem
        {
            Id = "page-id",
            Name = "Page Title",
            CreatedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            LastModifiedTime = new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero)
        };

        var result = service.AddFrontMatter("# Page Title\n\nBody", page);

        result.Replace("\r\n", "\n").Should().StartWith("""
            ---
            created: "2024-01-15 10:30 UTC"
            updated: "2024-02-20 14:45 UTC"
            ---

            # Page Title
            """.Replace("\r\n", "\n"));
    }

    [Fact]
    public void AddFrontMatter_WithMissingDates_ReturnsOriginalMarkdown()
    {
        var service = new YamlFrontMatterService();
        var page = new OneNoteItem
        {
            Id = "page-id",
            Name = "Page Title"
        };

        var result = service.AddFrontMatter("Body", page);

        result.Should().Be("Body");
        result.Should().NotStartWith("---");
        result.Should().NotContain("oneNotePageId:");
    }

    [Fact]
    public void AddFrontMatter_FormatsDatesAsHumanReadableUtc()
    {
        var service = new YamlFrontMatterService();
        var page = new OneNoteItem
        {
            Id = "page-id",
            Name = "Page Title",
            CreatedTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(3))
        };

        var result = service.AddFrontMatter("Body", page);

        result.Should().Contain("created: \"2024-01-15 07:30 UTC\"");
    }
}
