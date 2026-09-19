using System.Windows.Input;

namespace QuietRemind.Helpers;

public sealed class RelayCommand(Action execute) : ICommand
{
    // 命令始终可执行，无外部订阅者
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
