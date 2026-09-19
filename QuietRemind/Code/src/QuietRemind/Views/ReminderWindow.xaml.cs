using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

public partial class ReminderWindow : Window
{
    private readonly ReminderWindowViewModel _vm;
    private bool _allSettled;

    public ReminderWindow(ReminderWindowViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.AllSettled += OnAllSettled;
        Loaded += (_, _) => ApplyBackdrop();
        Closing += (_, e) =>
        {
            // 未全部收尾禁止关闭（需求 3.2.5：不允许 ESC / Alt+F4 / 关闭按钮直接关闭）
            if (!_allSettled)
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>按设置应用遮罩背景：自定义图片（可模糊）或深色底，叠加可调暗化层。</summary>
    private void ApplyBackdrop()
    {
        var settings = _vm.Settings;
        DimLayer.Opacity = Math.Clamp(settings.ReminderDimPercent, 0, 90) / 100.0;

        var path = settings.ReminderImagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            BackdropImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();

            BackdropImage.Source = bitmap;
            BackdropImage.Effect = settings.ReminderBlurRadius > 0
                ? new BlurEffect { Radius = Math.Clamp(settings.ReminderBlurRadius, 0, 40) }
                : null;
            BackdropImage.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // 背景图失效（格式/损坏）：回退深色底，不影响提醒
            BackdropImage.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAllSettled()
    {
        _allSettled = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // 屏蔽 ESC 与 Alt+F4（需求 3.2.5）
        if (e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.F4))
        {
            e.Handled = true;
        }
    }
}
