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
            // 未全部收尾禁止关闭（需求 3.2.6：不允许 ESC / Alt+F4 / 关闭按钮直接关闭）；
            // 预览模式可随时关闭，且从未写入数据。
            if (!_allSettled && !_vm.IsPreview)
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>向已打开的提醒窗追加条目，避免多个全屏提醒窗堆叠。</summary>
    public void AddEntries(IEnumerable<(QuietRemind.Models.ReminderTask Task, QuietRemind.Models.Occurrence Occ)> entries)
        => _vm.AddEntries(entries);

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
            // 先只读元数据计算解码尺寸，把宽高都限制在轻量范围内（避免超大/超窄图片解码占用飙升）
            var decoder = BitmapDecoder.Create(new Uri(path),
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var scale = Math.Min(1.0, 1920.0 / frame.PixelWidth);
            if (frame.PixelHeight * scale > 1080)
            {
                scale = 1080.0 / frame.PixelHeight;
            }
            var decodeWidth = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // 提醒背景只需铺满屏幕，限制解码宽度避免 4K/8K 原图全尺寸解码突破轻量内存目标
            bitmap.DecodePixelWidth = decodeWidth;
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
        if (_vm.IsPreview)
        {
            // 预览模式允许 ESC / Alt+F4 随时退出，不写入任何数据
            if (e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.F4))
            {
                Close();
            }
            return;
        }
        // 屏蔽 ESC 与 Alt+F4（需求 3.2.6）
        if (e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.F4))
        {
            e.Handled = true;
        }
    }

    private void OnClosePreview(object sender, RoutedEventArgs e) => Close();
}
