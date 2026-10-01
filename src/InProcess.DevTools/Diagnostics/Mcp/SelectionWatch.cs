using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Observes the change notifications a mutation is expected to raise (SelectionChanged, TextChanged,
    /// IsCheckedChanged). When the state changed but the framework raised nothing, the matching event is
    /// raised afterwards so view-models and handlers that depend on it behave as with user input.
    /// </summary>
    internal sealed class SelectionWatch : IDisposable
    {
        private readonly List<Watcher> _watchers = new();

        public readonly record struct Outcome(string[] Raised, string[] Synthesized);

        private sealed class Watcher
        {
            public required string Name { get; init; }

            public required Func<bool> Changed { get; init; }

            public required Action Synthesize { get; init; }

            public required IDisposable Subscription { get; init; }

            public bool Raised { get; set; }
        }

        /// <summary>Watcher for a property write through set_property; null-safe (empty) for unrelated properties.</summary>
        public static SelectionWatch For(AvaloniaObject node, string propertyName)
        {
            var watch = new SelectionWatch();

            switch (node)
            {
                case SelectingItemsControl or DataGrid when propertyName is "SelectedIndex" or "SelectedItem" or "SelectedValue" or "SelectedItems":
                    watch.AddSelection(node);
                    break;
                case Control container when propertyName == "IsSelected" && ItemsControl.ItemsControlFromItemContainer(container) is SelectingItemsControl owner:
                    watch.AddSelection(owner);
                    break;
                case TextBox textBox when propertyName == "Text":
                    watch.AddText(textBox);
                    break;
                case Avalonia.Controls.Primitives.ToggleButton toggle when propertyName == "IsChecked":
                    watch.AddChecked(toggle);
                    break;
            }

            return watch;
        }

        public static SelectionWatch ForSelection(AvaloniaObject node)
        {
            var watch = new SelectionWatch();
            watch.AddSelection(node);
            return watch;
        }

        private void AddSelection(AvaloniaObject node)
        {
            switch (node)
            {
                case SelectingItemsControl selecting:
                {
                    var before = selecting.SelectedItem;
                    Add("SelectionChanged", selecting, SelectingItemsControl.SelectionChangedEvent,
                        () => !Equals(before, selecting.SelectedItem),
                        () => selecting.RaiseEvent(new SelectionChangedEventArgs(SelectingItemsControl.SelectionChangedEvent, Wrap(before), Wrap(selecting.SelectedItem))));
                    break;
                }
                case DataGrid grid:
                {
                    var before = grid.SelectedItem;
                    Add("SelectionChanged", grid, DataGrid.SelectionChangedEvent,
                        () => !Equals(before, grid.SelectedItem),
                        () => grid.RaiseEvent(new SelectionChangedEventArgs(DataGrid.SelectionChangedEvent, Wrap(before), Wrap(grid.SelectedItem))));
                    break;
                }
            }
        }

        private void AddText(TextBox textBox)
        {
            var before = textBox.Text;
            Add("TextChanged", textBox, TextBox.TextChangedEvent,
                () => !string.Equals(before, textBox.Text, StringComparison.Ordinal),
                () => textBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent)));
        }

        private void AddChecked(Avalonia.Controls.Primitives.ToggleButton toggle)
        {
            var before = toggle.IsChecked;
            Add("IsCheckedChanged", toggle, Avalonia.Controls.Primitives.ToggleButton.IsCheckedChangedEvent,
                () => before != toggle.IsChecked,
                () => toggle.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.IsCheckedChangedEvent)));
        }

        private void Add<T>(string name, Interactive target, RoutedEvent<T> routedEvent, Func<bool> changed, Action synthesize)
            where T : RoutedEventArgs
        {
            Watcher? watcher = null;
            EventHandler<T> handler = (_, _) =>
            {
                if (watcher is not null)
                {
                    watcher.Raised = true;
                }
            };

            target.AddHandler(routedEvent, handler, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
            watcher = new Watcher
            {
                Name = name,
                Changed = changed,
                Synthesize = synthesize,
                Subscription = new Unsubscribe(() => target.RemoveHandler(routedEvent, handler))
            };
            _watchers.Add(watcher);
        }

        private static List<object> Wrap(object? item) => item is null ? new List<object>() : new List<object> { item };

        public Outcome Complete()
        {
            var synthesized = new List<string>();
            foreach (var watcher in _watchers)
            {
                if (!watcher.Raised && watcher.Changed())
                {
                    watcher.Synthesize();
                    synthesized.Add(watcher.Name);
                }
            }

            return new Outcome(
                _watchers.Where(w => w.Raised).Select(w => w.Name).ToArray(),
                synthesized.ToArray());
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Subscription.Dispose();
            }
        }

        private sealed class Unsubscribe : IDisposable
        {
            private Action? _action;

            public Unsubscribe(Action action) => _action = action;

            public void Dispose()
            {
                _action?.Invoke();
                _action = null;
            }
        }
    }
}
