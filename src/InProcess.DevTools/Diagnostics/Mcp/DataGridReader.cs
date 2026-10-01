using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// DataGrid helpers shared by reads (<c>get_items</c>, DOM text) and writes (cell editing).
    /// </summary>
    internal static class DataGridReader
    {
        public readonly record struct CellInfo(int ColumnIndex, string Header, string Text, bool Realized, bool Redacted);

        public static IEnumerable<DataGridColumn> VisibleColumns(DataGrid grid)
        {
            return grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex);
        }

        public static string ColumnHeader(DataGridColumn column)
        {
            return column.Header?.ToString() ?? string.Empty;
        }

        public static string? BoundPath(DataGridColumn column)
        {
            return column is DataGridBoundColumn { Binding: Avalonia.Data.Binding binding } ? binding.Path : null;
        }

        public static bool IsEditing(DataGrid grid)
        {
            return grid.GetVisualDescendants().OfType<DataGridRow>().Any(IsRowEditing);
        }

        public static bool IsRowEditing(DataGridRow row) => row.Classes.Contains(":editing");

        public static IEnumerable<CellInfo> ReadRow(DataGrid grid, DataGridRow row)
        {
            var item = row.DataContext;

            foreach (var column in VisibleColumns(grid))
            {
                var header = ColumnHeader(column);
                var path = BoundPath(column);
                var content = column.GetCellContent(row);

                if (NodeInfo.IsSensitiveName(header) || NodeInfo.IsSensitiveName(path) || (content is not null && NodeInfo.IsSensitive(content)))
                {
                    yield return new CellInfo(grid.Columns.IndexOf(column), header, NodeInfo.Redacted, content is not null, true);
                    continue;
                }

                if (content is not null)
                {
                    yield return new CellInfo(grid.Columns.IndexOf(column), header, NodeInfo.DisplayText(content), true, false);
                }
                else if (item is not null && path is not null && ValueReader.TryReadPath(item, path, out var value))
                {
                    yield return new CellInfo(grid.Columns.IndexOf(column), header, value?.ToString() ?? string.Empty, false, false);
                }
                else
                {
                    yield return new CellInfo(grid.Columns.IndexOf(column), header, string.Empty, false, false);
                }
            }
        }

        public static IEnumerable<DataGridRow> RealizedRows(DataGrid grid)
        {
            return grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(row => row.Index);
        }

        public static object? ItemAt(DataGrid grid, int index)
        {
            if (grid.ItemsSource is null || index < 0)
            {
                return null;
            }

            if (grid.ItemsSource is System.Collections.IList list)
            {
                return index < list.Count ? list[index] : null;
            }

            return grid.ItemsSource.Cast<object?>().Skip(index).FirstOrDefault();
        }

        public static int ItemCount(DataGrid grid)
        {
            return grid.ItemsSource is null ? 0 : ValueReader.Count(grid.ItemsSource);
        }

        /// <summary>Finds a column by zero-based index, header text or bound property path.</summary>
        public static DataGridColumn FindColumn(DataGrid grid, McpArgs args, string name)
        {
            var node = args.Node(name) ?? throw new McpToolException($"Argument '{name}' is required (column index or header text).");
            var all = grid.Columns;

            if (node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<int>(out var index))
            {
                if (index < 0 || index >= all.Count)
                {
                    throw new McpToolException($"column {index} is out of range; the grid has {all.Count} columns (0..{all.Count - 1}): {DescribeColumns(grid)}.");
                }

                return all[index];
            }

            var text = args.String(name)!;
            var matches = all.Where(c => string.Equals(ColumnHeader(c), text, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(BoundPath(c), text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0 && int.TryParse(text, out var parsed) && parsed >= 0 && parsed < all.Count)
            {
                return all[parsed];
            }

            return matches.Length > 0
                ? matches[0]
                : throw new McpToolException($"No column matches '{text}'. Columns: {DescribeColumns(grid)}.");
        }

        private static string DescribeColumns(DataGrid grid)
        {
            return string.Join(", ", grid.Columns.Select((c, i) => $"{i}:'{ColumnHeader(c)}'"));
        }
    }
}
