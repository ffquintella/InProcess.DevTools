using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private static DataGrid FindGrid(AvaloniaObject node)
        {
            return node as DataGrid
                   ?? (node as Visual)?.FindAncestorOfType<DataGrid>()
                   ?? throw new McpToolException($"{NodeTree.Describe(node)} is not a DataGrid and is not inside one. Target the DataGrid (or a node inside it).");
        }

        private object BeginEdit(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var grid = FindGrid(target.Node);
            var gridTarget = _tree.TargetFor(grid, target.Tree);

            var row = args.RequiredInt("row");
            var count = DataGridReader.ItemCount(grid);
            if (row < 0 || row >= count)
            {
                throw new McpToolException(count == 0
                    ? "The DataGrid has no rows (ItemsSource is empty or not set)."
                    : $"row {row} is out of range; the DataGrid has {count} rows (0..{count - 1}).");
            }

            var column = DataGridReader.FindColumn(grid, args, "column");
            var columnIndex = grid.Columns.IndexOf(column);

            if (grid.IsReadOnly)
            {
                throw new McpToolException("The DataGrid is read-only (IsReadOnly=true); cells cannot be edited.");
            }

            if (column.IsReadOnly)
            {
                throw new McpToolException($"Column {columnIndex} ('{DataGridReader.ColumnHeader(column)}') is read-only.");
            }

            var item = DataGridReader.ItemAt(grid, row);
            if (item is null || !BeginCellEdit(grid, item, column))
            {
                throw new McpToolException($"The DataGrid refused to start editing row {row}, column {columnIndex} ('{DataGridReader.ColumnHeader(column)}'): the previous edit may have failed validation, or the cell is not editable.");
            }

            var content = column.GetCellContent(item);
            var editingPath = content is null ? null : _tree.PathOf(content, target.Tree, out _);

            return new
            {
                target = gridTarget.DomId,
                gridPath = gridTarget.Path,
                row,
                column = columnIndex,
                header = DataGridReader.ColumnHeader(column),
                editing = true,
                editingPath,
                editingRootIndex = target.RootIndex,
                editingTree = target.Tree,
                editingType = content?.GetType().FullName,
                editingDomId = editingPath is null ? null : NodeTree.CreateDomId(target.RootIndex, editingPath)
            };
        }

        /// <summary>Makes the cell current and puts it into edit mode, creating its CellEditingTemplate content.</summary>
        private static bool BeginCellEdit(DataGrid grid, object? item, DataGridColumn column)
        {
            if (item is null)
            {
                return false;
            }

            grid.SelectedItem = item;
            grid.ScrollIntoView(item, column);
            grid.UpdateLayout();
            grid.CurrentColumn = column;
            grid.Focus();
            grid.UpdateLayout();
            return grid.BeginEdit();
        }

        private object CommitEdit(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var grid = FindGrid(target.Node);
            var wasEditing = DataGridReader.IsEditing(grid);
            var committed = grid.CommitEdit(EditingUnit(args), true);

            return new
            {
                target = _tree.TargetFor(grid, target.Tree).DomId,
                wasEditing,
                committed,
                isEditing = DataGridReader.IsEditing(grid)
            };
        }

        private object CancelEdit(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var grid = FindGrid(target.Node);
            var wasEditing = DataGridReader.IsEditing(grid);
            var cancelled = grid.CancelEdit(EditingUnit(args));

            return new
            {
                target = _tree.TargetFor(grid, target.Tree).DomId,
                wasEditing,
                cancelled,
                isEditing = DataGridReader.IsEditing(grid)
            };
        }

        private static DataGridEditingUnit EditingUnit(McpArgs args)
        {
            var unit = args.String("unit") ?? "row";
            return unit.ToLowerInvariant() switch
            {
                "row" => DataGridEditingUnit.Row,
                "cell" => DataGridEditingUnit.Cell,
                _ => throw new McpToolException($"Unknown unit '{unit}'. Use 'row' or 'cell'.")
            };
        }
    }
}
