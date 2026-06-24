using System.IO;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Resolves and validates the assets folder used for exported images and files.
    /// </summary>
    public static class AssetPathResolver
    {
        public const string DefaultAssetsFolderName = "_assets";

        public static string GetDefaultAssetsFolderPath(string outputPath)
        {
            return Path.Combine(ResolveOutputRoot(outputPath), DefaultAssetsFolderName);
        }

        public static string ResolveAssetsFolderPath(string outputPath, string? assetsFolderPath)
        {
            var outputRoot = ResolveOutputRoot(outputPath);
            var requestedPath = string.IsNullOrWhiteSpace(assetsFolderPath)
                ? Path.Combine(outputRoot, DefaultAssetsFolderName)
                : assetsFolderPath.Trim();

            return Path.GetFullPath(requestedPath, outputRoot);
        }

        public static string PrepareAssetsFolder(string outputPath, string? assetsFolderPath)
        {
            var resolvedPath = ResolveAssetsFolderPath(outputPath, assetsFolderPath);

            if (File.Exists(resolvedPath))
            {
                throw new IOException($"Assets folder path points to an existing file: {resolvedPath}");
            }

            Directory.CreateDirectory(resolvedPath);

            if (!Directory.Exists(resolvedPath))
            {
                throw new IOException($"Assets folder path is not a directory: {resolvedPath}");
            }

            return resolvedPath;
        }

        public static string GetRelativeAssetsPath(string markdownFolderPath, string assetsFolderPath)
        {
            return Path.GetRelativePath(markdownFolderPath, assetsFolderPath).Replace("\\", "/");
        }

        private static string ResolveOutputRoot(string outputPath)
        {
            var root = string.IsNullOrWhiteSpace(outputPath)
                ? ExportOptions.GetDefaultOutputPath()
                : outputPath.Trim();

            return Path.GetFullPath(root);
        }
    }
}
