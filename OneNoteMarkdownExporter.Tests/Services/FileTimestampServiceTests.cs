using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class FileTimestampServiceTests
{
    [Fact]
    public void ApplyTimestamps_WithCreatedTime_SetsCreationTimeUtc()
    {
        var service = new FileTimestampService();
        var filePath = CreateTempMarkdownFile();
        var created = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

        try
        {
            service.ApplyTimestamps(filePath, created, null);

            File.GetCreationTimeUtc(filePath).Should().BeCloseTo(created.UtcDateTime, TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ApplyTimestamps_WithModifiedTime_SetsLastWriteTimeUtc()
    {
        var service = new FileTimestampService();
        var filePath = CreateTempMarkdownFile();
        var modified = new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero);

        try
        {
            service.ApplyTimestamps(filePath, null, modified);

            File.GetLastWriteTimeUtc(filePath).Should().BeCloseTo(modified.UtcDateTime, TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ApplyTimestamps_WithBothTimes_SetsBothUtcTimestamps()
    {
        var service = new FileTimestampService();
        var filePath = CreateTempMarkdownFile();
        var created = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero);

        try
        {
            service.ApplyTimestamps(filePath, created, modified);

            File.GetCreationTimeUtc(filePath).Should().BeCloseTo(created.UtcDateTime, TimeSpan.FromSeconds(2));
            File.GetLastWriteTimeUtc(filePath).Should().BeCloseTo(modified.UtcDateTime, TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ApplyTimestamps_WithNullTimes_DoesNotChangeExistingTimestamps()
    {
        var service = new FileTimestampService();
        var filePath = CreateTempMarkdownFile();
        var originalCreated = File.GetCreationTimeUtc(filePath);
        var originalModified = File.GetLastWriteTimeUtc(filePath);

        try
        {
            service.ApplyTimestamps(filePath, null, null);

            File.GetCreationTimeUtc(filePath).Should().BeCloseTo(originalCreated, TimeSpan.FromSeconds(2));
            File.GetLastWriteTimeUtc(filePath).Should().BeCloseTo(originalModified, TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateTempMarkdownFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"timestamp_{Guid.NewGuid():N}.md");
        File.WriteAllText(filePath, "content");
        return filePath;
    }
}
