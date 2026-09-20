namespace SpaceMaid.App.Helpers;

/// <summary>
/// 极简 INotifyPropertyChanged 基类（与 CodeMemo 的 ViewModelBase 同款，保持工具族一致）。
/// ViewModel 只用它 + 平台接口，因此可以在没有 UI 线程的环境里被单测。
/// </summary>
public abstract class ViewModelBase : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
