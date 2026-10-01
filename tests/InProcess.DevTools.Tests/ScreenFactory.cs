using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;

namespace InProcess.DevTools.Tests;

/// <summary>Builds the "integration admin screen" used by the acceptance tests, in code.</summary>
public static class ScreenFactory
{
    public sealed record Screen(Window Window, ScreenViewModel ViewModel, TabControl Tabs, DataGrid Grid, TextBox Name, TextBox Password);

    public static Screen Create()
    {
        var vm = new ScreenViewModel();
        vm.Rows.Add(new ObjectRow { Name = "orders", Kind = "Databases" });
        vm.Rows.Add(new ObjectRow { Name = "billing", Kind = "Queues" });

        var kindColumn = new DataGridTemplateColumn
        {
            Header = "Kind",
            CellTemplate = new FuncDataTemplate<ObjectRow>((_, _) =>
            {
                var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                text.Bind(TextBlock.TextProperty, new Binding(nameof(ObjectRow.Kind)));
                return text;
            }),
            CellEditingTemplate = new FuncDataTemplate<ObjectRow>((_, _) =>
            {
                var combo = new ComboBox { ItemsSource = vm.Kinds };
                combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(ObjectRow.Kind)) { Mode = BindingMode.TwoWay });
                return combo;
            })
        };

        var tokenColumn = new DataGridTemplateColumn
        {
            Header = "Credential",
            CellTemplate = new FuncDataTemplate<ObjectRow>((_, _) =>
            {
                var box = new TextBox { PasswordChar = '*', IsReadOnly = true };
                box.Bind(TextBox.TextProperty, new Binding(nameof(ObjectRow.Token)));
                return box;
            })
        };

        var grid = new DataGrid
        {
            Name = "ObjectsGrid",
            ItemsSource = vm.Rows,
            AutoGenerateColumns = false,
            Height = 200
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(ObjectRow.Name)), IsReadOnly = true });
        grid.Columns.Add(kindColumn);
        grid.Columns.Add(tokenColumn);

        var name = new TextBox { Name = "NameBox" };
        name.Bind(TextBox.TextProperty, new Binding(nameof(ScreenViewModel.Title)) { Mode = BindingMode.TwoWay });

        var password = new TextBox { Name = "PasswordBox", PasswordChar = '*', Text = "s3cr3t-value" };

        var runButton = new Button { Name = "RunButton", Content = "Run" };
        runButton.Bind(Button.CommandProperty, new Binding(nameof(ScreenViewModel.Run)));

        var check = new CheckBox { Name = "FlagCheck", Content = "Flag" };
        check.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(ScreenViewModel.Flag)) { Mode = BindingMode.TwoWay });

        var objectsTab = new TabItem { Header = "Objects", Content = new StackPanel { Children = { grid } } };
        var settingsTab = new TabItem
        {
            Header = "Settings",
            Content = new StackPanel { Children = { name, password, runButton, check } }
        };

        var tabs = new TabControl { Name = "MainTabs", Items = { objectsTab, settingsTab } };
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs)
            {
                vm.TabChanged = true;
            }
        };
        tabs.Bind(TabControl.SelectedIndexProperty, new Binding(nameof(ScreenViewModel.SelectedTab)) { Mode = BindingMode.TwoWay });

        var window = new Window
        {
            Width = 800,
            Height = 600,
            DataContext = vm,
            Content = tabs
        };

        return new Screen(window, vm, tabs, grid, name, password);
    }
}
