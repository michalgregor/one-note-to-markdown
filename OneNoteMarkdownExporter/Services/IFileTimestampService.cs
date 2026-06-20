using System;

namespace OneNoteMarkdownExporter.Services
{
    public interface IFileTimestampService
    {
        void ApplyTimestamps(string filePath, DateTimeOffset? created, DateTimeOffset? modified);
    }
}
