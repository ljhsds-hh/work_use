using CodeMemo.Helpers;

namespace CodeMemo.ViewModels;

/// <summary>详情页里一个待填参数的输入项：名字来自命令中的 &lt;xxx&gt;，值由用户填写。</summary>
public sealed class PlaceholderInput : ViewModelBase
{
    private readonly Action _valueChanged;

    public PlaceholderInput(string name, Action valueChanged)
    {
        Name = name;
        _valueChanged = valueChanged;
    }

    /// <summary>参数名（不含尖括号）。</summary>
    public string Name { get; }

    /// <summary>用户填写的值；每次变化都会让详情页的「复制预览」重新生成。</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _valueChanged();
            }
        }
    }
    private string _value = "";
}
