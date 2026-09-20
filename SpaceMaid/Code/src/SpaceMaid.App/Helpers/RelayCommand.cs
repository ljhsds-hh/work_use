using System.Windows.Input;

namespace SpaceMaid.App.Helpers;

/// <summary>无参命令（按钮绑定用）。</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

/// <summary>带参命令（分级相关的"全选/反选"用）。</summary>
public sealed class RelayCommand<T>(Action<T?> execute) : ICommand
{
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute((T?)parameter);
}
