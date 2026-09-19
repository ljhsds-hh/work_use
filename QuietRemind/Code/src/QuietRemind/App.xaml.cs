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
    private DateOnly _lastPlanDate;

    private const string LogDir = @"D:\logs\QuietRemind";

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuietRemind");

    private static string NsFilePath => Path.Combine(LogDir, "mutex.ns");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            // 启动失败必须退出：全局异常处理器会 Handled 掉异常导致"无窗口无托盘的僵尸进程"，
            // 提醒机制静默死亡（需求 9.3）。此处显式提示并终止。
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(Path.Combine(LogDir, $"startup-error-{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:HH:mm:ss}] 启动失败：{ex}\n");
            }
            catch (IOException)
            {
                // 日志目录不可用时忽略
            }
            MessageBox.Show($"QuietRemind 启动失败，程序将退出。\n\n{ex.Message}", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var isScheduled = e.Args.Contains(TaskSchedulerGuard.ScheduledArg);

        // 单实例（需求 7.3）：已有实例时，手动启动唤起主界面，守护触发静默退出
        _singleInstance = new SingleInstance(NsFilePath);
        if (!_singleInstance.IsPrimary)
        {
            if (!isScheduled)
            {
                SingleInstance.SignalShowMain(NsFilePath);
            }
            Shutdown();
            return;
        }

        var log = new LogService(LogDir);
        log.Info($"QuietRemind 启动，参数：[{string.Join(" ", e.Args)}]");

        // 退出标记存放日志目录：该目录在用户会话进程与计划任务守护进程间文件视图一致
        // （部分环境对 %AppData% 新写入文件存在进程视图隔离，见 JsonStore 注释）
        var store = new JsonStore(DataDir, Path.Combine(LogDir, "exit.marker"));
        var load = store.Load();
        var data = load.Data;
        if (load.Problems.Count > 0)
        {
            // 损坏/读取失败按域隔离：仅提示受影响文件，其余数据照常使用
            log.Error($"数据加载异常：{string.Join("；", load.Problems)}");
            if (!isScheduled)
            {
                MessageBox.Show(
                    $"部分数据文件加载异常，本次会话不会覆写这些文件（磁盘现场已保留）。\n\n{string.Join("\n", load.Problems)}",
                    "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        log.Info($"数据目录：{DataDir}，退出标记存在：{store.HasExitMarker()}");

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

        // 退出标记（需求 7.2 / 7.4）：守护语义遇标记不拉起；登录/手动语义清除标记。
        // 登录语义 = 本次开机周期内第一次计划任务启动（boot.marker 记录上次处理时刻，
        // 以系统启动时间为界；用户在实例运行后的退出写入标记，由后续守护语义尊重）。
        var isLoginStartup = isScheduled && MarkBootLoginIfNeeded();
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

        // 提醒事件 → 弹窗；错过判定实时落盘（需求 8.2：错过判定属状态变更）
        engine.RemindersDue += (due, missed) =>
        {
            if (missed.Count > 0)
            {
                _services.PersistOccurrences();
            }
            ShowReminder(due, missed);
        };

        // 系统事件（需求 4 章关机拦截 / 5 章睡眠错过）
        SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // 启动序列：补生成实例 → 落盘 → 错过扫描（需求 5.2）
        _lastPlanDate = DateOnly.FromDateTime(clock.Now);
        planner.EnsureUpTo(data.Occurrences, data.Tasks);
        _services.PersistOccurrences();
        engine.ScanMissed(data.Occurrences, data.Tasks, clock.Now);
        log.Info($"启动完成：任务 {data.Tasks.Count} 条，实例 {data.Occurrences.Count} 条");

        // 秒级轮询（需求 3.1）
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

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
        // 实例补齐仅在跨天时执行（新增实例只可能出现在新的一天；任务增删改路径已显式重建）
        var today = DateOnly.FromDateTime(_services.Clock.Now);
        if (today != _lastPlanDate)
        {
            var added = _services.Planner.EnsureUpTo(data.Occurrences, data.Tasks);
            if (added > 0)
            {
                _services.PersistOccurrences();
            }
            _lastPlanDate = today;
        }
        _services.Engine.Poll(data.Occurrences, data.Tasks);

        // 单实例唤醒检查：重复手动启动时弹出主界面（并入 1s 轮询，无常驻后台线程）
        if (_singleInstance?.TryConsumeShowMainSignal() == true)
        {
            ShowMainWindow();
        }
    }

    private void ShowReminder(IReadOnlyList<Occurrence> due, IReadOnlyList<Occurrence> missed)
    {
        if (_services is null)
        {
            return;
        }
        // 到期与错过实例合并进同一个全屏提醒窗口，逐条带各自徽标（需求 3.4.1）
        var entries = due.Concat(missed)
            .Select(o => (Task: _services.Data.Tasks.FirstOrDefault(t => t.Id == o.TaskId), Occ: o))
            .Where(x => x.Task is not null)
            .Select(x => (x.Task!, x.Occ))
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        _services.Log.Info($"提醒展示：到期 {due.Count} 条，错过 {missed.Count} 条");
        var vm = new ReminderWindowViewModel(_services, entries);
        var win = new ReminderWindow(vm);
        win.Closed += (_, _) => _mainWindow?.RefreshRows();
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
        // SystemEvents 在专用后台线程触发：必须封送回 UI 线程，避免与轮询并发读写共享状态
        if (_services is null || e.Mode != PowerModes.Resume)
        {
            return;
        }
        Dispatcher.Invoke(() =>
        {
            _services.Log.Info("系统从睡眠/休眠唤醒，执行错过扫描");
            _services.Engine.ScanMissed(_services.Data.Occurrences, _services.Data.Tasks, _services.Clock.Now);
        });
    }

    /// <summary>
    /// 登录语义判定与登记：本次开机周期内（系统启动时间晚于上次登记时刻）的第一次计划任务启动
    /// 视为登录触发（等价开机自启，需求 7.4.3），登记后本开机周期内的后续触发均为守护语义。
    /// </summary>
    private static bool MarkBootLoginIfNeeded()
    {
        try
        {
            var boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            var flagPath = Path.Combine(LogDir, "boot.marker");
            var last = File.Exists(flagPath) ? DateTime.Parse(File.ReadAllText(flagPath)) : DateTime.MinValue;
            if (last >= boot)
            {
                return false; // 本开机周期已处理过登录语义
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(flagPath, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            return true;
        }
        catch (Exception)
        {
            // 判定失败时按登录语义处理（宁可多拉起，不可漏提醒，需求 1.2.1）
            return true;
        }
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

        try
        {
            // 写入用户主动退出标记后退出（需求 7.4）
            _services.Store.WriteExitMarker();
            _services.Log.Info("用户确认退出，已写入主动退出标记");
        }
        catch (Exception ex)
        {
            // 标记写入失败也必须允许退出，否则用户点退出无响应（需求 9.3）
            _services.Log.Error("退出标记写入失败", ex);
        }
        ExitApplication();
    }

    private void ExitApplication()
    {
        _pollTimer?.Stop();
        _tray?.Dispose();
        if (_services is not null)
        {
            _services.Log.Info("QuietRemind 退出");
            _services.Log.Dispose();
        }
        Shutdown();
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
