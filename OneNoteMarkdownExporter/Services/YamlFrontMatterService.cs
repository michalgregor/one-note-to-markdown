using System;
using System.Globalization;
using System.Text;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public interface IYamlFrontMatterService
    {
        string AddFrontMatter(string markdown, OneNoteItem page);
    }

    public sealed class YamlFrontMatterService : IYamlFrontMatterService
    {
        public string AddFrontMatter(string markdown, OneNoteItem page)
        {
            if (!page.CreatedTime.HasValue && !page.LastModifiedTime.HasValue)
            {
                return markdown;
            }

            var builder = new StringBuilder();
            builder.AppendLine("---");

            if (page.CreatedTime.HasValue)
            {
                builder.AppendLine($"created: {FormatDate(page.CreatedTime.Value)}");
            }

            if (page.LastModifiedTime.HasValue)
            {
                builder.AppendLine($"updated: {FormatDate(page.LastModifiedTime.Value)}");
            }

            builder.AppendLine("---");
            builder.AppendLine();
            builder.Append(markdown);

            return builder.ToString();
        }

        private static string FormatDate(DateTimeOffset value)
        {
            return FormatYamlString(value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }

        private static string FormatYamlString(string value)
        {
            var escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            return $"\"{escaped}\"";
        }
    }
}
