using System.Collections.Generic;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public static class ExportSelectionHelper
    {
        public static int CountPagesToExport(IEnumerable<OneNoteItem> items, bool isImplicitlySelected = false)
        {
            var count = 0;

            foreach (var item in items)
            {
                var isSelected = item.IsSelected || isImplicitlySelected;
                var hasSelectedDescendants = HasSelectedDescendants(item);

                if (!isSelected && !hasSelectedDescendants)
                {
                    continue;
                }

                if (item.Type == OneNoteItemType.Page && isSelected)
                {
                    count++;
                }

                count += CountPagesToExport(item.Children, isSelected);
            }

            return count;
        }

        public static int CountItemsToExport(IEnumerable<OneNoteItem> items, bool isImplicitlySelected = false)
        {
            var count = 0;

            foreach (var item in items)
            {
                var isSelected = item.IsSelected || isImplicitlySelected;
                if (isSelected || HasSelectedDescendants(item))
                {
                    count++;
                    count += CountItemsToExport(item.Children, isSelected);
                }
            }

            return count;
        }

        public static bool HasSelectedDescendants(OneNoteItem item)
        {
            foreach (var child in item.Children)
            {
                if (child.IsSelected || HasSelectedDescendants(child)) return true;
            }

            return false;
        }
    }
}
