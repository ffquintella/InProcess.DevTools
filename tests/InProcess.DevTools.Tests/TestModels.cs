using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace InProcess.DevTools.Tests;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class ObjectRow : Observable
{
    private string _kind = "Databases";
    private string _secret = "hunter2";

    public string Name { get; set; } = "";

    public string Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string Token
    {
        get => _secret;
        set => Set(ref _secret, value);
    }
}

public sealed class ScreenViewModel : Observable
{
    private int _selectedTab;
    private bool _tabChanged;
    private string _title = "";
    private bool _loaded;
    private bool _flag;
    private int _executed;

    public ObservableCollection<ObjectRow> Rows { get; } = new();

    public string[] Kinds { get; } = { "Databases", "Servers", "Queues" };

    public int SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    public bool TabChanged
    {
        get => _tabChanged;
        set => Set(ref _tabChanged, value);
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Loaded
    {
        get => _loaded;
        set => Set(ref _loaded, value);
    }

    public bool Flag
    {
        get => _flag;
        set => Set(ref _flag, value);
    }

    public int Executed
    {
        get => _executed;
        set => Set(ref _executed, value);
    }

    public ICommand Run { get; }

    public ScreenViewModel()
    {
        Run = new DelegateCommand(() => Executed++);
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action _action;

        public DelegateCommand(Action action) => _action = action;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _action();
    }
}
