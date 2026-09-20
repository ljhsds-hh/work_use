using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CodeMemo.Models;
using CodeMemo.Services;
using CodeMemo.ViewModels;
using HandyControl.Data;
using Growl = HandyControl.Controls.Growl;

namespace CodeMemo.Views;

/// <summary>
/// 主窗口：三栏布局（分类树 + 外观偏好 / 快捷复制面板 + 命令列表 / 详情），
/// 负责对话框、快捷键、托盘常驻、全局热键与 Growl 提示的接线。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IDialogService _dialogs;
    private readonly AppPlatform _platform;
    private bool _reallyExit;
    private bool _trayHintShown;

    public MainWindow(LibraryStore store, SettingsStore settingsStore, AppSettings settings, AppPlatform platform)
    {
        InitializeComponent();
        _platform = platform;
        _dialogs = platform.Dialogs;
        _viewModel = new MainViewModel(store, settingsStore, settings, platform);
        DataContext = _viewModel;

        _viewModel.Notified += ShowGrowl;
        _viewModel.RequestAdd += OnRequestAdd;
        _viewModel.RequestEdit += OnRequestEdit;
        _viewModel.LoadProblemReported += message => _dialogs.ShowWarning(message, "数据加载提示");
        _viewModel.HotkeyPressed += OnHotkeyPressed;

        // 轻提示宿主：注册到主窗口根部面板，Growl.Info 带 token 显示
        Growl.Register(Growl.GetToken(RootPanel), RootPanel);
        _growlToken = Growl.GetToken(RootPanel);
    }

    private readonly string _growlToken = "";

    private void ShowGrowl(string message) => Growl.Info(message, _growlToken);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Initialize();
        SetupTrayIcon();

        // 默认选中「全部命令」根节点
        if (_viewModel.FilterNodes.Count > 0)
        {
            FilterTree.ItemContainerGenerator.StatusChanged += OnTreeContainerReady;
            FilterTree.UpdateLayout();
            SelectFirstNode();
        }
        SearchBox.Focus();
    }

    /// <summary>托盘图标：取不到应用图标也不影响启动，HandyControl 会退回自带图标。</summary>
    private void SetupTrayIcon()
    {
        try
        {
            TrayIcon.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        }
        catch (Exception)
        {
            // 图标缺失/解码失败都无所谓，继续用默认图标
        }

        try
        {
            TrayIcon.Init();
        }
        catch (Exception)
        {
            // 托盘创建失败不该拖垮主流程
        }
    }

    private void OnTreeContainerReady(object? sender, EventArgs e)
    {
        if (FilterTree.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
        {
            SelectFirstNode();
            FilterTree.ItemContainerGenerator.StatusChanged -= OnTreeContainerReady;
        }
    }

    private void SelectFirstNode()
    {
        if (FilterTree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem item)
        {
            item.IsSelected = true;
        }
    }

    private void OnFilterTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FilterNode node)
        {
            _viewModel.SelectedNode = node;
        }
    }

    /// <summary>
    /// 热键录制：录制态下吃掉窗口收到的按键，把「修饰键 + 主键」转成手势文本交给 ViewModel 校验。
    /// 只按修饰键时继续等；Esc 取消。
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_viewModel.IsRecordingHotkey)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        // Alt 组合键在 WPF 里以 Key.System 上报，真实主键在 SystemKey
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _viewModel.CancelHotkeyRecording();
            ShowGrowl("已取消录制全局热键");
            return;
        }

        var gesture = HotkeyRecorder.FromKeyboard(key, Keyboard.Modifiers);
        if (gesture is null)
        {
            return;   // 只按了修饰键（或主键不支持），继续等
        }

        _viewModel.CancelHotkeyRecording();
        if (_viewModel.TrySetHotkey(gesture))
        {
            ShowGrowl($"全局热键已改为 {gesture}");
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 录制热键时不做任何快捷键动作
        if (_viewModel.IsRecordingHotkey)
        {
            base.OnKeyDown(e);
            return;
        }

        // 快捷键（需求 6.3 + 常规键盘流）：Ctrl+F 搜索、Ctrl+N 新增、Delete 删除、Esc 清空搜索
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            _viewModel.AddCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // 在输入框里按 Delete 是在删字，别把当前命令删了
        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBoxBase)
        {
            _viewModel.DeleteCommand.Execute(_viewModel.SelectedCommand);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _viewModel.IsSearching)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>列表里回车即复制当前选中命令。</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && _viewModel.SelectedCommand is { } row)
        {
            _viewModel.CopyCommand.Execute(row);
            e.Handled = true;
        }
    }

    /// <summary>双击某一行即复制（点在列表空白处不响应）。</summary>
    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(CommandList, source) is ListBoxItem
            && _viewModel.SelectedCommand is { } row)
        {
            _viewModel.CopyCommand.Execute(row);
            e.Handled = true;
        }
    }

    // ---------- 托盘常驻 / 全局热键 ----------

    /// <summary>热键触发：把窗口捞回前台并聚焦搜索框。</summary>
    private void OnHotkeyPressed() => Dispatcher.Invoke(ShowFromTray);

    private void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnTrayActivate(object sender, RoutedEventArgs e)
    {
        // 已经在前台就收起来，否则显示出来（一个图标兼顾开关）
        if (IsVisible && IsActive)
        {
            Hide();
            return;
        }
        ShowFromTray();
    }

    private void OnTrayShowHide(object sender, RoutedEventArgs e)
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowFromTray();
        }
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        Close();
    }

    /// <summary>托盘菜单：打开数据目录（数据文件、备份与 settings.json 都在那里）。</summary>
    private void OnTrayOpenDataDirectory(object sender, RoutedEventArgs e)
        => _viewModel.OpenDataDirectoryCommand.Execute(null);

    protected override void OnClosing(CancelEventArgs e)
    {
        // 常驻模式下关闭只收进托盘，真正退出走托盘菜单
        if (!_reallyExit && _viewModel.MinimizeToTrayEnabled)
        {
            e.Cancel = true;
            Hide();

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                try
                {
                    TrayIcon.ShowBalloonTip("CodeMemo 仍在后台运行",
                        "双击托盘图标或用全局热键呼出窗口；要从托盘菜单才能退出", NotifyIconInfoType.Info);
                }
                catch (Exception)
                {
                    // 气泡提示不可用就算了
                }
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 关窗（真正退出）前把节流待写的使用统计落盘，别让刚复制的那几次统计丢了
        try
        {
            _viewModel.FlushPendingSaves();
        }
        catch (Exception)
        {
            // 退出路径不抛异常
        }

        try
        {
            _platform.Hotkey.Dispose();
        }
        catch (Exception)
        {
            // 退出路径不抛异常
        }

        try
        {
            TrayIcon.Dispose();
        }
        catch (Exception)
        {
            // 同上
        }

        base.OnClosed(e);
    }

    private void OnRequestAdd()
    {
        var (category, group) = _viewModel.CurrentFilterCategory;
        var dialog = new CommandEditorWindow(null, category, group, _dialogs) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SavedEntry is { } entry)
        {
            _viewModel.AddEntry(entry);
            ShowGrowl("已新增命令");
        }
    }

    private void OnRequestEdit(CommandEntry entry)
    {
        var dialog = new CommandEditorWindow(entry, null, null, _dialogs) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.UpdateEntry(entry);
            ShowGrowl("已保存修改");
        }
    }
}
