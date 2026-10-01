using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace InProcess.DevTools.Mcp
{
    internal sealed partial class InProcessDevToolsMcpServer
    {
        private object Focus(McpArgs args)
        {
            var target = _tree.Resolve(args);

            if (target.Node is not Control control)
            {
                throw new McpToolException("The target node is not focusable.");
            }

            return new
            {
                target = target.DomId,
                focused = control.Focus()
            };
        }

        private object Click(McpArgs args)
        {
            var target = _tree.Resolve(args);
            RequireControl(target);
            var effect = InvokeClick(target.Node);

            return new
            {
                target = target.DomId,
                eventName = "click",
                raised = true,
                effect
            };
        }

        private object DoubleClick(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var control = RequireControl(target);

            // A real double click is two clicks on a button; for selectable targets one selection is enough.
            var clicks = control is Button ? 2 : 1;
            string effect = string.Empty;
            for (var i = 0; i < clicks; i++)
            {
                effect = InvokeClick(control);
            }

            var doubleTapped = RaiseDoubleTapped(control);
            string? editingPath = null;
            var startedEdit = false;

            if (control.FindAncestorOfType<DataGridCell>(includeSelf: true) is { } cell
                && cell.FindAncestorOfType<DataGrid>() is { } grid
                && !grid.IsReadOnly)
            {
                var row = cell.FindAncestorOfType<DataGridRow>();
                var column = ColumnOfCell(grid, row, cell);
                if (row is not null && column is not null && !column.IsReadOnly)
                {
                    if (!DataGridReader.IsEditing(grid))
                    {
                        startedEdit = BeginCellEdit(grid, DataGridReader.ItemAt(grid, row.Index) ?? row.DataContext, column);
                    }

                    var content = column.GetCellContent(row.DataContext!);
                    editingPath = content is null ? null : _tree.PathOf(content, target.Tree, out _);
                }
            }

            return new
            {
                target = target.DomId,
                eventName = "doubleClick",
                raised = true,
                doubleTappedRaised = doubleTapped,
                startedEdit,
                editingPath,
                effect
            };
        }

        private object RaiseSupportedEvent(McpArgs args)
        {
            var eventName = args.String("eventName") ?? "click";
            var target = _tree.Resolve(args);
            RequireControl(target);

            switch (eventName.ToLowerInvariant())
            {
                case "click":
                    InvokeClick(target.Node);
                    break;
                case "focus":
                    ((Control)target.Node).Focus();
                    break;
                default:
                    throw new McpToolException($"Event '{eventName}' is not supported by the MCP server. Supported: click, focus.");
            }

            return new
            {
                target = target.DomId,
                eventName,
                raised = true
            };
        }

        private static Control RequireControl(NodeTree.Target target)
        {
            return target.Node as Control
                   ?? throw new McpToolException($"The target node {target.DomId} must be a Control, but is {target.Node.GetType().FullName}.");
        }

        private static string InvokeClick(AvaloniaObject node)
        {
            switch (node)
            {
                case ToggleButton toggleButton:
                    toggleButton.IsChecked = toggleButton.IsChecked != true;
                    if (toggleButton.Command?.CanExecute(toggleButton.CommandParameter) == true)
                    {
                        toggleButton.Command.Execute(toggleButton.CommandParameter);
                    }

                    return $"toggled; IsChecked={toggleButton.IsChecked}";
                case Button button:
                    if (button.Command?.CanExecute(button.CommandParameter) == true)
                    {
                        button.Command.Execute(button.CommandParameter);
                    }

                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return "clicked";
                case MenuItem menuItem:
                    if (menuItem.Command?.CanExecute(menuItem.CommandParameter) == true)
                    {
                        menuItem.Command.Execute(menuItem.CommandParameter);
                    }

                    menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    return "clicked";
                case DataGridCell or DataGridRow or DataGridRowHeader:
                    return SelectDataGridRow((Control)node);
                case Control container when ItemsControl.ItemsControlFromItemContainer(container) is SelectingItemsControl owner && container is not SelectingItemsControl:
                    return SelectContainer(owner, container);
                case Control control:
                    control.Focus();
                    return "focused";
                default:
                    throw new McpToolException("The target node does not support click navigation.");
            }
        }

        private static string SelectContainer(SelectingItemsControl owner, Control container)
        {
            if (!container.IsEffectivelyEnabled)
            {
                throw new McpToolException($"{NodeTree.Describe(container)} is disabled and cannot be selected by a user.");
            }

            var index = owner.IndexFromContainer(container);
            if (index < 0)
            {
                throw new McpToolException($"{NodeTree.Describe(container)} is not a realised item of {NodeTree.Describe(owner)}.");
            }

            owner.SelectedIndex = index;
            return $"selected index {index} of {NodeTree.Describe(owner)}";
        }

        private static string SelectDataGridRow(Control cellOrRow)
        {
            var grid = cellOrRow.FindAncestorOfType<DataGrid>()
                       ?? throw new McpToolException("The target is not inside a DataGrid.");
            var row = cellOrRow as DataGridRow ?? cellOrRow.FindAncestorOfType<DataGridRow>()
                      ?? throw new McpToolException("The target is not inside a DataGridRow.");

            if (row.DataContext is { } item)
            {
                grid.SelectedItem = item;
            }

            if (cellOrRow is DataGridCell cell && ColumnOfCell(grid, row, cell) is { } column)
            {
                grid.CurrentColumn = column;
                return $"selected row {row.Index}, column {grid.Columns.IndexOf(column)}";
            }

            return $"selected row {row.Index}";
        }

        private static DataGridColumn? ColumnOfCell(DataGrid grid, DataGridRow? row, DataGridCell cell)
        {
            if (row is null)
            {
                return null;
            }

            var cells = row.GetVisualDescendants().OfType<DataGridCell>().ToList();
            var position = cells.IndexOf(cell);
            var columns = DataGridReader.VisibleColumns(grid).ToList();
            return position >= 0 && position < columns.Count ? columns[position] : null;
        }

        private static bool RaiseDoubleTapped(Control control)
        {
            try
            {
                if (TopLevel.GetTopLevel(control) is not { } root)
                {
                    return false;
                }

                var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root) ?? default;
                var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
                var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
                var released = new PointerReleasedEventArgs(control, pointer, root, center, (ulong)Environment.TickCount64, properties, KeyModifiers.None, MouseButton.Left);
                var tapped = new TappedEventArgs(InputElement.DoubleTappedEvent, released) { Source = control };
                control.RaiseEvent(tapped);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ----- set_property -----

        private object SetProperty(McpArgs args)
        {
            var propertyName = args.String("propertyName");
            var value = args.Node("value");
            var target = _tree.Resolve(args);
            var raiseEvents = args.Bool("raise_events");

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new McpToolException("propertyName is required.");
            }

            if (raiseEvents && !Extended(_options.EnableMutationEvents))
            {
                throw new McpToolException("raise_events requires McpServerOptions.EnableMutationEvents (and is unavailable in optimized builds unless AllowInReleaseBuilds is set).");
            }

            var property = target.Node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite)
            {
                throw new McpToolException($"Property '{propertyName}' is not a writable CLR property on {target.Node.GetType().FullName}.");
            }

            object? convertedValue;
            try
            {
                convertedValue = ConvertJsonValue(value, property.PropertyType);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or InvalidOperationException)
            {
                throw new McpToolException($"Cannot convert {value?.ToJsonString()} to {property.PropertyType.Name} for property '{propertyName}': {ex.Message}");
            }

            using var change = raiseEvents ? SelectionWatch.For(target.Node, propertyName) : null;
            property.SetValue(target.Node, convertedValue);
            var raised = change?.Complete() ?? new SelectionWatch.Outcome(Array.Empty<string>(), Array.Empty<string>());

            return new
            {
                target = target.DomId,
                propertyName,
                value = NodeInfo.IsSensitive(target.Node) ? NodeInfo.Redacted : convertedValue?.ToString(),
                updated = true,
                eventsRaised = raised.Raised,
                eventsSynthesized = raised.Synthesized
            };
        }

        private static object? ConvertJsonValue(System.Text.Json.Nodes.JsonNode? value, Type targetType)
        {
            if (value is null)
            {
                return null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            var actualType = nullableType ?? targetType;

            if (actualType == typeof(string))
            {
                return value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var text) ? text : value.ToJsonString();
            }

            if (actualType == typeof(bool))
            {
                return value.GetValue<bool>();
            }

            if (actualType == typeof(int))
            {
                return value.GetValue<int>();
            }

            if (actualType == typeof(double))
            {
                return value.GetValue<double>();
            }

            if (actualType.IsEnum)
            {
                return Enum.Parse(actualType, value.GetValue<string>(), ignoreCase: true);
            }

            if (actualType == typeof(object))
            {
                return value is System.Text.Json.Nodes.JsonValue any && any.TryGetValue<object>(out var raw) ? raw : value.ToString();
            }

            return Convert.ChangeType(value.ToString(), actualType, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ----- select_item -----

        private object SelectItem(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var node = target.Node;

            var criteria = new[] { args.Has("index"), args.Has("value"), args.Has("text") }.Count(c => c);
            if (criteria != 1)
            {
                throw new McpToolException("Provide exactly one of `index`, `value` or `text`.");
            }

            // A path to a tab/list item selects within its parent.
            if (node is Control container && node is not SelectingItemsControl && node is not DataGrid
                && ItemsControl.ItemsControlFromItemContainer(container) is SelectingItemsControl owner)
            {
                var effect = SelectContainer(owner, container);
                return new { target = target.DomId, selectedIndex = owner.SelectedIndex, effect, selectionChangedRaised = true };
            }

            ItemsControl? itemsControl = node as SelectingItemsControl;
            var grid = node as DataGrid;
            if (itemsControl is null && grid is null)
            {
                throw new McpToolException($"{NodeTree.Describe(node)} ({node.GetType().FullName}) does not support selection; target a ComboBox, ListBox, TabControl, DataGrid or one of their items.");
            }

            var items = grid is not null
                ? (grid.ItemsSource?.Cast<object?>().ToList() ?? new List<object?>())
                : itemsControl!.Items.Cast<object?>().ToList();

            string TextOf(object? item) => grid is not null ? NodeInfo.DisplayItemText(grid, item) : NodeInfo.ItemText(itemsControl!, item);

            var matches = new List<int>();
            if (args.Has("index"))
            {
                var index = args.RequiredInt("index");
                if (index < 0 || index >= items.Count)
                {
                    throw new McpToolException($"index {index} is out of range; {NodeTree.Describe(node)} has {items.Count} items{Preview(items.Select(TextOf))}.");
                }

                matches.Add(index);
            }
            else if (args.Has("text"))
            {
                var text = args.String("text")!;
                for (var i = 0; i < items.Count; i++)
                {
                    if (string.Equals(TextOf(items[i]), text, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(i);
                    }
                }

                if (matches.Count == 0)
                {
                    throw new McpToolException($"No item with text '{text}' in {NodeTree.Describe(node)}; items{Preview(items.Select(TextOf))}.");
                }
            }
            else
            {
                var wanted = args.Node("value")!;
                var wantedText = wanted is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var s) ? s : wanted.ToJsonString();
                var valuePath = (itemsControl as SelectingItemsControl)?.SelectedValueBinding is Avalonia.Data.Binding { Path: { Length: > 0 } p } ? p : null;
                for (var i = 0; i < items.Count; i++)
                {
                    object? candidate = items[i];
                    if (valuePath is not null && candidate is not null && ValueReader.TryReadPath(candidate, valuePath, out var read))
                    {
                        candidate = read;
                    }

                    var candidateText = candidate switch
                    {
                        bool b => b ? "true" : "false",
                        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                        _ => candidate?.ToString()
                    };
                    if (string.Equals(candidateText, wantedText, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(i);
                    }
                }

                if (matches.Count == 0)
                {
                    throw new McpToolException($"No item with value '{wantedText}' in {NodeTree.Describe(node)}; items{Preview(items.Select(TextOf))}.");
                }
            }

            var chosen = matches[0];
            var chosenItem = items[chosen];

            if (itemsControl is not null && itemsControl.ContainerFromIndex(chosen) is Control { IsEffectivelyEnabled: false } disabled)
            {
                throw new McpToolException($"Item {chosen} ('{TextOf(chosenItem)}') is disabled and cannot be selected by a user.");
            }

            using var watch = SelectionWatch.ForSelection(node);
            if (grid is not null)
            {
                grid.SelectedItem = chosenItem;
                if (chosenItem is not null)
                {
                    grid.ScrollIntoView(chosenItem, null);
                }
            }
            else
            {
                var selecting = (SelectingItemsControl)itemsControl!;
                selecting.SelectedIndex = chosen;
                if (selecting is ComboBox comboBox)
                {
                    comboBox.IsDropDownOpen = false;
                }
            }

            var outcome = watch.Complete();
            return new
            {
                target = target.DomId,
                selectedIndex = grid?.SelectedIndex ?? (itemsControl as SelectingItemsControl)?.SelectedIndex,
                selectedText = TextOf(chosenItem),
                matches = matches.Count,
                selectionChangedRaised = outcome.Raised.Length > 0 || outcome.Synthesized.Length > 0,
                eventsSynthesized = outcome.Synthesized
            };
        }

        private static string Preview(IEnumerable<string> texts)
        {
            var all = texts.ToList();
            var shown = string.Join(", ", all.Take(20).Select(t => $"'{t}'"));
            return all.Count == 0 ? " (none)" : $" ({all.Count}): {shown}{(all.Count > 20 ? ", ..." : string.Empty)}";
        }

        // ----- invoke_command -----

        private object InvokeCommand(McpArgs args)
        {
            var target = _tree.Resolve(args);
            var commandProperty = args.String("commandProperty") ?? "Command";

            var type = target.Node.GetType();
            var property = type.GetProperty(commandProperty, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                var available = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
                    .Select(p => p.Name)
                    .ToArray();
                throw new McpToolException($"{type.FullName} has no public ICommand property '{commandProperty}'. ICommand properties: {(available.Length == 0 ? "none" : string.Join(", ", available))}.");
            }

            var parameterName = commandProperty == "Command" ? "CommandParameter" : commandProperty + "Parameter";
            var parameter = type.GetProperty(parameterName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target.Node);

            if (property.GetValue(target.Node) is not ICommand command)
            {
                throw new McpToolException($"{commandProperty} on {NodeTree.Describe(target.Node)} is not bound (null).");
            }

            if (!command.CanExecute(parameter))
            {
                throw new McpToolException($"{commandProperty} on {NodeTree.Describe(target.Node)} cannot execute right now (CanExecute returned false).");
            }

            command.Execute(parameter);
            return new { target = target.DomId, commandProperty, executed = true };
        }
    }
}
