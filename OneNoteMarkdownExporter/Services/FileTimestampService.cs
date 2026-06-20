using System;
using System.IO;

namespace OneNoteMarkdownExporter.Services
{
    public sealed class FileTimestampService : IFileTimestampService
    {
        public void ApplyTimestamps(string filePath, DateTimeOffset? created, DateTimeOffset? modified)
        {
            if (created.HasValue)
            {
                File.SetCreationTimeUtc(filePath, created.Value.UtcDateTime);
            }

            if (modified.HasValue)
            {
                File.SetLastWriteTimeUtc(filePath, modified.Value.UtcDateTime);
            }
        }
    }
}
