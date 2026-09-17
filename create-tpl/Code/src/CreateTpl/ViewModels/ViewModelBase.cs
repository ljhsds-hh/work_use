using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CreateTpl.ViewModels;

/// <summary>
/// ViewModel 基类：实现 INotifyPropertyChanged，供数据绑定刷新。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>标准属性赋值：值有变化时才写入并通知，返回是否发生了变更。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
