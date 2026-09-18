using System.IO;
using System.Windows;
using System.Windows.Threading;
using HandyControl.Controls;
using Microsoft.Win32;
using QuietRemind.Models;
using QuietRemind.Services;
using QuietRemind.ViewModels;
using QuietRemind.Views;
using MessageBox = HandyControl.Controls.MessageBox;

namespace QuietRemind;

public partial class App : Application
{
    private AppServices? _services;
    private SingleInstance? _singleInstance;
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _tray;
    private DispatcherTimer? _pollTimer;
    private MainWindow? _mainWindow;

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuietRemind");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // 单实例（需求 7.3）：已有实例时，手动启动唤起主界面，守护触发静默退出
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsPrimary)
        {
            if (!e.Args.Contains(TaskSchedulerGuard.ScheduledArg))
            {
                SingleInstance.SignalShowMain();
            }
            Shutdown();
            return;
        }

        var log = new LogService(@"D:\logs\QuietRemind");
        log.Info($"QuietRemind 启动，参数：[{string.Join(" ", e.Args)}]");

        var store = new JsonStore(DataDir);
        AppData data;
        try
        {
            data = store.Load();
        }
        catch (InvalidDataException ex)
        {
            log.Error("数据文件损坏，按空数据启动", ex);
            MessageBox.Show($"数据文件损坏，损坏文件已备份，本次以空数据启动。\n\n{ex.Message}",
                "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
            data = new AppData();
        }

        var clock = SystemClock.Instance;
        var engine = new ReminderEngine(clock);
        var planner = new OccurrencePlanner(clock);
        var guard = new TaskSchedulerGuard(Environment.ProcessPath ?? "QuietRemind", log);
        _services = new AppServices
        {
            Store = store,
            Data = data,
            Clock = clock,
            Engine = engine,
            Planner = planner,
            Log = log,
            Guard = guard,
        };

        // 退出标记（需求 7.2 / 7.4）：守护触发遇标记不拉起；登录/手动启动清除标记
        var isScheduled = e.Args.Contains(TaskSchedulerGuard.ScheduledArg);
        var isLoginStartup = isScheduled && IsRecentBootStartup();
        if (store.HasExitMarker())
        {
            if (isScheduled && !isLoginStartup)
            {
                log.Info("守护触发但存在用户主动退出标记，本轮不拉起（静默退出）");
                Shutdown();
                return;
            }
            store.ClearExitMarker();
            log.Info("登录/手动启动：清除用户主动退出标记");
        }

        // 进程守护注册（需求 7.1，设置默认开启；失败不阻断本次运行）
        if (data.Settings.GuardEnabled && !guard.EnsureRegistered())
        {
            log.Warn("计划任务注册失败，本次运行不受影响");
        }

        // 提醒事件 → 弹窗
        engine.NormalRemindersDue += occs => ShowReminder(occs, missed: false);
        engine.MissedRemindersDue += occs => ShowReminder(occs, missed: true);

        // 系统事件（需求 4 章关机拦截 / 5 章睡眠错过）
        SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // 启动序列：补生成实例 → 错过扫描（需求 5.2）
        planner.EnsureUpTo(data.Occurrences, data.Tasks);
        store.SaveOccurrences(data.Occurrences);
        engine.ScanMissed(data.Occurrences, data.Tasks, clock.Now);
        log.Info($"启动完成：任务 {data.Tasks.Count} 条，实例 {data.Occurrences.Count} 条");

        // 秒级轮询（需求 3.1）
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // 单实例唤醒监听：重复手动启动时弹出主界面
        _singleInstance.StartShowMainWatcher(() => Dispatcher.Invoke(ShowMainWindow));

        // 托盘（需求 6.2）
        CreateTray();

        // 手动启动显示主界面；计划任务启动静默进托盘（需求 7.2.2 / 5.2.5）
        if (!isScheduled)
        {
            ShowMainWindow();
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_services is null)
        {
            return;
        }
        var data = _services.Data;
        var added = _services.Planner.EnsureUpTo(data.Occurrences, data.Tasks);
        if (added > 0)
        {
            _services.Persist();
        }
        _services.Engine.Poll(data.Occurrences, data.Tasks);
    }

    private void ShowReminder(IReadOnlyList<Occurrence> occs, bool missed)
    {
        if (_services is null)
        {
            return;
        }
        var entries = occs
            .Select(o => (Task: _services.Data.Tasks.FirstOrDefault(t => t.Id == o.TaskId), Occ: o))
            .Where(x => x.Task is not null)
            .Select(x => (x.Task!, x.Occ))
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        _services.Log.Info($"{(missed ? "错过补提醒" : "到点提醒")}：{entries.Count} 条");
        var vm = new ReminderWindowViewModel(_services, entries);
        var win = new ReminderWindow(vm);
        win.Show();
        win.Activate();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_services is null)
        {
            return;
        }

        var now = _services.Clock.Now;
        var owed = ShutdownIntercept.GetOwedOccurrences(now, _services.Data.Tasks, _services.Data.Occurrences);
        if (owed.Count == 0)
        {
            _services.Log.Info("关机/注销：无欠账任务，静默放行");
            return;
        }

        var entries = owed
            .Select(o => (_services.Data.Tasks.First(t => t.Id == o.TaskId), o))
            .ToList();
        _services.Log.Info($"关机/注销拦截：{owed.Count} 条欠账任务");
        var vm = new ShutdownInterceptWindowViewModel(entries);
        var win = new ShutdownInterceptWindow(vm);
        // 同步模态：系统等待本进程回复 WM_QUERYENDSESSION 期间，用户可在此窗口处理
        var cancelShutdown = win.ShowDialog() == true;
        if (cancelShutdown)
        {
            e.Cancel = true;
            _services.Log.Info("用户选择【取消关机】，本次关机已中止");
        }
        else
        {
            _services.Log.Info("用户选择【仍要关机】，放行关机流程");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_services is null || e.Mode != PowerModes.Resume)
        {
            return;
        }
        _services.Log.Info("系统从睡眠/休眠唤醒，执行错过扫描");
        _services.Engine.ScanMissed(_services.Data.Occurrences, _services.Data.Tasks, _services.Clock.Now);
    }

    private void CreateTray()
    {
        var icon = LoadAppIcon();
        _tray = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "QuietRemind 任务提醒",
        };

        var menu = new System.Windows.Controls.ContextMenu();
        var open = new System.Windows.Controls.MenuItem { Header = "打开主界面" };
        open.Click += (_, _) => ShowMainWindow();
        var exit = new System.Windows.Controls.MenuItem { Header = "立即退出" };
        exit.Click += (_, _) => ConfirmExit();
        menu.Items.Add(open);
        menu.Items.Add(exit);
        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var uri = new Uri("pack://application:,,,/QuietRemind;component/Assets/app.ico");
        using var stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException("缺少应用图标资源");
        return new System.Drawing.Icon(stream);
    }

    private void ShowMainWindow()
    {
        if (_services is null)
        {
            return;
        }
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(new MainViewModel(_services), _services);
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ConfirmExit()
    {
        if (_services is null)
        {
            return;
        }
        var result = MessageBox.Show(
            "退出后进程守护不再拉起，本次开机周期内到点提醒与错过检测均不生效，重启电脑后自动恢复。\n\n确定要退出吗？",
            "立即退出", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        // 写入用户主动退出标记后退出（需求 7.4）
        _services.Store.WriteExitMarker();
        _services.Log.Info("用户确认退出，已写入主动退出标记");
        ExitApplication();
    }

    private void ExitApplication()
    {
        _pollTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        if (_services is not null)
        {
            _services.Log.Info("QuietRemind 退出");
            _services.Log.Dispose();
        }
        Shutdown();
    }

    private static bool IsRecentBootStartup()
    {
        // 登录触发发生在系统刚启动的 2 分钟内；守护触发在任意时刻
        try
        {
            var boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            return (DateTime.Now - boot).TotalMinutes < 2;
        }
        catch
        {
            return false;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Log.Error("未处理异常（UI 线程）", e.Exception);
        if (_mainWindow?.IsVisible == true)
        {
            MessageBox.Show($"程序遇到异常：{e.Exception.Message}\n异常详情已写入日志。",
                "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _services?.Log.Error("未处理异常（非 UI 线程）", ex);
        }
    }
}
