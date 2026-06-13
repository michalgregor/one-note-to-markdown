using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OneNoteMarkdownExporter.Services
{
    public static class ExportPathSanitizer
    {
        public const int MaxWin32PathLength = 259;
        public const int MaxWin32DirectoryPathLength = MaxWin32PathLength - 14;

        private const int MaxComponentLength = 255;
        private const int HashLength = 8;
        private const string DefaultFallbackName = "Untitled";

        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "COM\u00b9", "COM\u00b2", "COM\u00b3",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
        };

        public static string SanitizeComponent(string? name, string fallbackName = DefaultFallbackName, string? stableId = null)
        {
            var sanitized = SanitizeRawComponent(name);
            if (IsEmptySanitizedName(sanitized))
            {
                sanitized = SanitizeRawComponent(fallbackName);
            }

            if (IsEmptySanitizedName(sanitized))
            {
                sanitized = DefaultFallbackName;
            }

            return ProtectReservedName(sanitized);
        }

        public static string GetMarkdownFileName(string? pageName, string? pageId = null)
        {
            return $"{SanitizeComponent(pageName, DefaultFallbackName, pageId)}.md";
        }

        public static string GetSafeDirectoryPath(string parentPath, string? componentName, string? stableId = null, string fallbackName = DefaultFallbackName)
        {
            var component = SanitizeComponent(componentName, fallbackName, stableId);
            var safeComponent = FitComponentToPath(parentPath, component, string.Empty, stableId ?? componentName ?? fallbackName, MaxWin32DirectoryPathLength);

            return Path.Combine(parentPath, safeComponent);
        }

        public static string GetSafeMarkdownFilePath(string folderPath, string? pageName, string? pageId = null, int copyCounter = 0)
        {
            var stem = SanitizeComponent(pageName, DefaultFallbackName, pageId);
            if (copyCounter > 0)
            {
                stem = $"{stem} ({copyCounter})";
            }

            var safeFileName = FitComponentToPath(folderPath, stem, ".md", pageId ?? pageName ?? DefaultFallbackName, MaxWin32PathLength);

            return Path.Combine(folderPath, safeFileName);
        }

        public static string GetSafeAssetFileName(string assetsFolder, string? pagePrefix, int imageIndex, string extension)
        {
            var normalizedExtension = NormalizeExtension(extension);
            var imageStem = $"image_{imageIndex:D4}";
            var stem = string.IsNullOrWhiteSpace(pagePrefix)
                ? imageStem
                : $"{SanitizeComponent(pagePrefix, "page", pagePrefix)}_{imageStem}";

            return FitComponentToPath(assetsFolder, stem, normalizedExtension, pagePrefix ?? imageStem, MaxWin32PathLength);
        }

        public static string GetSafeAttachmentFileName(string assetsFolder, string? pagePrefix, string? preferredName, int attachmentIndex, string extension)
        {
            var normalizedExtension = NormalizeExtension(extension);
            var nameStem = SanitizeComponent(Path.GetFileNameWithoutExtension(preferredName ?? string.Empty), "attachment", preferredName);
            // Always include the index so two attachments with the same name on one page don't collide.
            var indexedStem = $"{nameStem}_{attachmentIndex:D4}";
            var stem = string.IsNullOrWhiteSpace(pagePrefix)
                ? indexedStem
                : $"{SanitizeComponent(pagePrefix, "page", pagePrefix)}_{indexedStem}";

            return FitComponentToPath(assetsFolder, stem, normalizedExtension, pagePrefix + preferredName + attachmentIndex, MaxWin32PathLength);
        }

        private static string SanitizeRawComponent(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var sanitized = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                sanitized.Append(IsInvalidWindowsFileNameCharacter(character) ? '_' : character);
            }

            var result = Regex.Replace(sanitized.ToString(), "_{2,}", "_").Trim();
            result = TrimTrailingSpacesAndPeriods(result);

            return result;
        }

        private static string TrimTrailingSpacesAndPeriods(string value)
        {
            var length = value.Length;
            while (length > 0 && (char.IsWhiteSpace(value[length - 1]) || value[length - 1] == '.'))
            {
                length--;
            }

            return value[..length];
        }

        private static bool IsInvalidWindowsFileNameCharacter(char character)
        {
            return character == '<'
                || character == '>'
                || character == ':'
                || character == '"'
                || character == '/'
                || character == '\\'
                || character == '|'
                || character == '?'
                || character == '*'
                || character <= 31;
        }

        private static bool IsEmptySanitizedName(string name)
        {
            return string.IsNullOrWhiteSpace(name) || name.Trim('_').Length == 0;
        }

        private static string ProtectReservedName(string component)
        {
            var firstPeriodIndex = component.IndexOf('.');
            var stem = firstPeriodIndex >= 0 ? component[..firstPeriodIndex] : component;

            if (!ReservedNames.Contains(stem))
            {
                return component;
            }

            return firstPeriodIndex >= 0
                ? $"{stem}_{component[firstPeriodIndex..]}"
                : $"{component}_";
        }

        private static string FitComponentToPath(string parentPath, string stem, string extension, string stableInput, int maxPathLength)
        {
            extension = NormalizeExtension(extension);
            var availableLength = GetAvailableComponentLength(parentPath, maxPathLength);
            var maxComponentLength = Math.Min(MaxComponentLength, availableLength);

            if (maxComponentLength < extension.Length + 1)
            {
                throw new PathTooLongException($"The export path is too long to create a file or folder under '{parentPath}'.");
            }

            var fullComponent = $"{stem}{extension}";
            if (fullComponent.Length <= maxComponentLength)
            {
                return fullComponent;
            }

            return ShortenComponent(stem, extension, maxComponentLength, stableInput);
        }

        private static int GetAvailableComponentLength(string parentPath, int maxPathLength)
        {
            var fullParentPath = Path.GetFullPath(parentPath);
            var separatorLength = fullParentPath.EndsWith(Path.DirectorySeparatorChar) || fullParentPath.EndsWith(Path.AltDirectorySeparatorChar)
                ? 0
                : 1;

            return maxPathLength - fullParentPath.Length - separatorLength;
        }

        private static string ShortenComponent(string stem, string extension, int maxComponentLength, string stableInput)
        {
            var maxStemLength = maxComponentLength - extension.Length;
            if (maxStemLength <= 0)
            {
                throw new PathTooLongException("The export path is too long to preserve the file extension.");
            }

            var hash = GetStableHash(stableInput);
            var suffix = $"_{hash}";
            if (maxStemLength <= suffix.Length)
            {
                return $"{hash[..maxStemLength]}{extension}";
            }

            var prefixLength = maxStemLength - suffix.Length;
            var prefix = stem.Length <= prefixLength ? stem : stem[..prefixLength];
            prefix = prefix.TrimEnd(' ', '.', '_');

            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = DefaultFallbackName[..Math.Min(DefaultFallbackName.Length, prefixLength)];
            }

            return $"{prefix}{suffix}{extension}";
        }

        private static string GetStableHash(string stableInput)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableInput));
            return string.Concat(bytes.Take(HashLength / 2).Select(value => value.ToString("x2")));
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith('.') ? extension : $".{extension}";
        }
    }
}
