using System.Windows.Media;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>设置面板里的一个主题色样：色点预览随明暗模式变化，选中态由 MainViewModel 统一维护。</summary>
public sealed class ThemeAccentOptionViewModel : ViewModelBase
{
    private readonly Action<ThemeAccentOptionViewModel> _select;
    private bool _isCurrent;
    private Brush _previewBrush;

    public ThemeAccentOptionViewModel(ThemeAccentInfo info, bool isCurrent, Action<ThemeAccentOptionViewModel> select)
    {
        Key = info.Key;
        Name = info.Name;
        _isCurrent = isCurrent;
        _select = select;
        _previewBrush = CreateBrush(ThemeMode.Light);
        SelectCommand = new RelayCommand(() => _select(this));
    }

    public string Key { get; }

    public string Name { get; }

    /// <summary>色点实际颜色：取当前模式下的主色，避免深色模式仍显示深绿导致“所见不符”。</summary>
    public Brush PreviewBrush
    {
        get => _previewBrush;
        private set => SetProperty(ref _previewBrush, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public RelayCommand SelectCommand { get; }

    public void UpdatePreview(ThemeMode mode) => PreviewBrush = CreateBrush(mode);

    private Brush CreateBrush(ThemeMode mode)
    {
        var accent = ThemePalette.Build(mode, Key).Brushes["BAccent"];
        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        return brush;
    }
}
