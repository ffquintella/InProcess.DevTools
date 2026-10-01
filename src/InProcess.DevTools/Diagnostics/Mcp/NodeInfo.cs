using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Reads human-facing text and state from controls. Everything that can leak a secret goes through here
    /// so redaction is applied in a single place.
    /// </summary>
    internal static class NodeInfo
    {
        public const string Redacted = "***REDACTED***";

        private static readonly Regex SensitiveName = new(
            "pass(word|wd|phrase)?$|^pwd|secret|token|api[-_]?key|credential|private[-_]?key|connection[-_]?string",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsSensitive(AvaloniaObject node)
        {
            if (node is TextBox { PasswordChar: not '\0' })
            {
                return true;
            }

            return node.GetValue(McpRedaction.IsSensitiveProperty);
        }

        public static bool IsSensitiveName(string? name) => !string.IsNullOrEmpty(name) && SensitiveName.IsMatch(name);

        /// <summary>Type names (short and full) of the node and all of its base types.</summary>
        public static IEnumerable<Type> TypeChain(object node)
        {
            for (var type = node.GetType(); type is not null && type != typeof(object); type = type.BaseType!)
            {
                yield return type;
            }
        }

        public static bool IsInteractive(AvaloniaObject node)
        {
            return node switch
            {
                Button or ToggleButton or TextBox or ComboBox or ComboBoxItem or ListBoxItem or TabItem or MenuItem
                    or Slider or DataGrid or DataGridRow or DataGridCell or DataGridColumnHeader or TreeViewItem
                    or AutoCompleteBox or NumericUpDown or DatePicker or CalendarDatePicker => true,
                InputElement { Focusable: true, IsEnabled: true } => true,
                _ => false
            };
        }

        /// <summary>The short text shown for a node in DOM snapshots.</summary>
        public static string? GetText(AvaloniaObject node)
        {
            if (IsSensitive(node))
            {
                return node is TextBox { Text.Length: 0 } or TextBlock { Text.Length: 0 } ? string.Empty : Redacted;
            }

            return node switch
            {
                TextBlock textBlock => textBlock.Text,
                TextBox textBox => textBox.Text,
                ComboBox comboBox => comboBox.SelectedItem is null ? null : ItemText(comboBox, comboBox.SelectedItem),
                DataGridRow row => RowText(row),
                HeaderedContentControl headered => headered.Header is Control ? null : headered.Header?.ToString(),
                ContentControl contentControl => contentControl.Content is Control ? null : contentControl.Content?.ToString(),
                _ => null
            };
        }

        /// <summary>
        /// The text a user would read from a control, looking into its content when it has none of its own.
        /// </summary>
        public static string DisplayText(Control control)
        {
            if (IsSensitive(control))
            {
                return Redacted;
            }

            switch (control)
            {
                case TextBlock textBlock:
                    return textBlock.Text ?? string.Empty;
                case TextBox textBox:
                    return textBox.Text ?? string.Empty;
                case ComboBox comboBox:
                    return comboBox.SelectedItem is null ? string.Empty : ItemText(comboBox, comboBox.SelectedItem);
                case CheckBox { Content: null } checkBox:
                    return checkBox.IsChecked is { } c ? (c ? "true" : "false") : "indeterminate";
                case TabItem tabItem when tabItem.Header is not Control:
                    return tabItem.Header?.ToString() ?? string.Empty;
                case ContentControl { Content: not null and not Control } contentControl:
                    return contentControl.Content?.ToString() ?? string.Empty;
                case NumericUpDown numeric:
                    return numeric.Value?.ToString() ?? string.Empty;
                case AutoCompleteBox autoComplete:
                    return autoComplete.Text ?? string.Empty;
            }

            var parts = control.GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c is TextBlock or TextBox)
                .Select(c => c is TextBlock || c.GetVisualAncestors().OfType<Control>().All(a => a == control || a is not TextBox)
                    ? DisplayText(c)
                    : null)
                .Where(text => !string.IsNullOrEmpty(text))
                .Take(8)
                .ToArray();

            return string.Join(" ", parts!);
        }

        /// <summary>Display text of a data item inside an items control.</summary>
        public static string ItemText(ItemsControl owner, object? item)
        {
            if (item is null)
            {
                return string.Empty;
            }

            if (item is string text)
            {
                return text;
            }

            var container = owner.ContainerFromItem(item);
            if (container is Control realised)
            {
                var shown = DisplayText(realised);
                if (!string.IsNullOrEmpty(shown))
                {
                    return shown;
                }
            }

            if (owner.DisplayMemberBinding is Avalonia.Data.Binding { Path: { Length: > 0 } path })
            {
                var value = ValueReader.TryReadPath(item, path, out var read) ? read : null;
                return value?.ToString() ?? string.Empty;
            }

            return item switch
            {
                TabItem tab => tab.Header?.ToString() ?? string.Empty,
                ContentControl { Content: not Control } content => content.Content?.ToString() ?? string.Empty,
                _ => item.ToString() ?? string.Empty
            };
        }

        /// <summary>Display text of a data item of a DataGrid: its realised row, or the item's own text.</summary>
        public static string DisplayItemText(DataGrid grid, object? item)
        {
            if (item is null)
            {
                return string.Empty;
            }

            var row = DataGridReader.RealizedRows(grid).FirstOrDefault(r => ReferenceEquals(r.DataContext, item));
            return row is not null ? RowText(row) : item.ToString() ?? string.Empty;
        }

        public static string RowText(DataGridRow row)
        {
            var grid = row.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault();
            if (grid is null)
            {
                return string.Empty;
            }

            return string.Join(" | ", DataGridReader.ReadRow(grid, row).Select(cell => cell.Text));
        }

        public static object GetState(AvaloniaObject node)
        {
            var sensitive = IsSensitive(node);
            return node switch
            {
                TextBox textBox => new { text = sensitive ? (string.IsNullOrEmpty(textBox.Text) ? textBox.Text : Redacted) : textBox.Text },
                ToggleButton toggleButton => new { isChecked = toggleButton.IsChecked },
                ComboBox comboBox => new
                {
                    selectedIndex = comboBox.SelectedIndex,
                    selectedItem = comboBox.SelectedItem is null ? null : (sensitive ? Redacted : ItemText(comboBox, comboBox.SelectedItem)),
                    itemCount = comboBox.ItemCount,
                    isDropDownOpen = comboBox.IsDropDownOpen
                },
                SelectingItemsControl selecting => new
                {
                    selectedIndex = selecting.SelectedIndex,
                    selectedItem = selecting.SelectedItem is null ? null : (sensitive ? Redacted : selecting.SelectedItem.ToString()),
                    itemCount = selecting.ItemCount
                },
                DataGrid grid => (object)new
                {
                    selectedIndex = grid.SelectedIndex,
                    selectedItem = grid.SelectedItem is null ? null : (sensitive ? Redacted : grid.SelectedItem.ToString()),
                    columnCount = grid.Columns.Count,
                    isReadOnly = grid.IsReadOnly,
                    isEditing = DataGridReader.IsEditing(grid)
                },
                DataGridRow row => new { index = row.Index, isSelected = row.IsSelected, isEditing = DataGridReader.IsRowEditing(row) },
                ItemsControl items => new { itemCount = items.ItemCount },
                TabItem tabItem => new { isSelected = tabItem.IsSelected },
                RangeBase rangeBase => new
                {
                    value = rangeBase.Value,
                    minimum = rangeBase.Minimum,
                    maximum = rangeBase.Maximum
                },
                _ => new { }
            };
        }
    }
}
