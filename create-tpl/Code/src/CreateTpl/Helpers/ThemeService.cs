using System.Windows;

namespace CreateTpl.Helpers;

/// <summary>
/// 主题服务：切换 Blueprint Studio 暗色/亮色主题令牌与 HandyControl 皮肤。
/// 所有界面颜色一律引用 Tokens.*.xaml 中的 DynamicResource 令牌，切换即全局生效。
/// </summary>
public static class ThemeService
{
    /// <summary>当前是否为暗色主题（默认暗色，与 App.xaml 初始合并字典一致）。</summary>
    public static bool IsDark { get; private set; } = true;

    public static void Apply(bool dark)
    {
        IsDark = dark;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();

        // HandyControl 皮肤（Growl / 基础控件随主题联动）
        var skin = dark ? "SkinDark" : "SkinDefault";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{skin}.xaml")
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml")
        });

        // Blueprint Studio 主题令牌
        var tokens = dark ? "Tokens.Dark" : "Tokens.Light";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/CreateTpl;component/Themes/{tokens}.xaml")
        });
    }
}
