using System;
using System.Collections.Generic;
using System.IO;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public sealed class PageExportContext
    {
        public OneNoteItem Page { get; init; } = new();
        public string MarkdownFolderPath { get; init; } = string.Empty;
        public string MarkdownFilePath { get; init; } = string.Empty;
        public string ChildPageFolderPath { get; init; } = string.Empty;
        public string AssetsFolderPath { get; init; } = string.Empty;
        public string RelativeAssetsPath { get; init; } = string.Empty;
    }

    public sealed class ExportPathPlanner
    {
        private readonly string _outputRoot;
        private readonly ExportOptions _options;
        private readonly Dictionary<string, HashSet<string>> _claimedAssetNamesByParent = new(StringComparer.OrdinalIgnoreCase);

        public ExportPathPlanner(string outputRoot, ExportOptions options)
        {
            _outputRoot = string.IsNullOrWhiteSpace(outputRoot)
                ? ExportOptions.GetDefaultOutputPath()
                : Path.GetFullPath(outputRoot);
            _options = options;
        }

        public string OutputRoot => _outputRoot;

        public string GetContainerFolderPath(string parentPath, OneNoteItem container)
        {
            return ExportPathSanitizer.GetSafeDirectoryPath(parentPath, container.Name, container.Id);
        }

        public string GetChildPageFolderPath(string parentPath, OneNoteItem page)
        {
            return ExportPathSanitizer.GetSafeDirectoryPath(parentPath, page.Name, page.Id);
        }

        public string GetCentralizedAssetsFolderPath()
        {
            return AssetPathResolver.ResolveAssetsFolderPath(_outputRoot, _options.AssetsFolderPath);
        }

        public string GetScopedAssetsFolderPath(string parentPath, OneNoteItem scope)
        {
            var claimedNames = GetClaimedNames(parentPath);
            return ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(parentPath, AssetPathResolver.DefaultAssetsFolderName, scope.Name, scope.Id, claimedNames);
        }

        public PageExportContext CreatePageContext(OneNoteItem page, string markdownFolderPath, string assetsFolderPath)
        {
            return new PageExportContext
            {
                Page = page,
                MarkdownFolderPath = markdownFolderPath,
                MarkdownFilePath = ExportPathSanitizer.GetSafeMarkdownFilePath(markdownFolderPath, page.Name, page.Id),
                ChildPageFolderPath = GetChildPageFolderPath(markdownFolderPath, page),
                AssetsFolderPath = assetsFolderPath,
                RelativeAssetsPath = AssetPathResolver.GetRelativeAssetsPath(markdownFolderPath, assetsFolderPath)
            };
        }

        private HashSet<string> GetClaimedNames(string parentPath)
        {
            var fullParentPath = Path.GetFullPath(parentPath);
            if (!_claimedAssetNamesByParent.TryGetValue(fullParentPath, out var claimedNames))
            {
                claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _claimedAssetNamesByParent[fullParentPath] = claimedNames;
            }

            return claimedNames;
        }
    }
}
