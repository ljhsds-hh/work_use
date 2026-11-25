using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
    private ReminderWindow? _reminderWindow;
    private QuickAddWindow? _quickAddWindow;
    private bool _shutdownInterceptActive;
    private readonly List<(ReminderTask Task, Occurrence Occ)> _deferredReminders = [];
    private DateTime _lastPersistenceWarning = DateTime.MinValue;
    private long _lastDeferredFlushTick;
    private volatile bool _exiting;
    private volatile bool _resumeScanRequired;
    private DateOnly _lastPlanDate;

    /// <summary>首选日志目录（需求 10 章）；不可用时 ResolveLogDir 会回退到数据目录。</summary>
    private const string LogDir = @"D:\logs\QuietRemind";

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuietRemind");

    /// <summary>运行中标记：正常退出会删除；残留说明上次被强制结束或原生崩溃（无托管异常记录）。</summary>
    private static string RunningFlagPath => Path.Combine(DataDir, "running.flag");

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
            var logDir = ResolveLogDir();
            TryAppendEarlyLog(logDir, $"启动失败：{ex}", "ERROR");
            MessageBox.Show($"QuietRemind 启动失败，程序将退出。\n\n{ex.Message}", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // 备份导入后的自重启：新实例先等旧进程退出，避免单实例互斥把新实例挡回去
        WaitForRestartPredecessor(e.Args);

        var isScheduled = e.Args.Contains(TaskSchedulerGuard.ScheduledArg);
        var logDir = ResolveLogDir();

        // 单实例（需求 7.3）：已有实例时，手动启动唤起主界面，守护触发静默退出
        _singleInstance = new SingleInstance(Path.Combine(logDir, "mutex.ns"));
        if (!_singleInstance.IsPrimary)
        {
            if (!isScheduled)
            {
                SingleInstance.SignalShowMain(Path.Combine(logDir, "mutex.ns"));
                TryAppendEarlyLog(logDir, "手动重复启动：已唤起已有实例主界面，本实例退出");
            }
            else
            {
                TryAppendEarlyLog(logDir, "守护触发：已有主实例在运行，本轮跳过");
            }
            Shutdown();
            return;
        }

        var log = new LogService(logDir);
        log.Info($"QuietRemind 启动，参数：[{string.Join(" ", e.Args)}]");
        MarkRunning(log);

        // 退出标记存放日志目录：该目录在用户会话进程与计划任务守护进程间文件视图一致
        // （部分环境对 %AppData% 新写入文件存在进程隔离，见 JsonStore 注释）。
        // 日志目录不可用时 ResolveLogDir 已回退到数据目录，保证标记链路不失效。
        var store = new JsonStore(DataDir, Path.Combine(logDir, "exit.marker"));
        var load = store.Load();
        var data = load.Data;
        var sanitizeWarnings = DataSanitizer.Normalize(data);

        if (load.Problems.Count > 0 || sanitizeWarnings.Count > 0)
        {
            // 损坏/读取失败按域隔离：仅提示受影响文件，其余数据照常使用
            foreach (var problem in load.Problems)
            {
                log.Error($"数据加载异常：{problem}");
            }
            foreach (var warning in sanitizeWarnings)
            {
                log.Warn($"数据归一化：{warning}");
            }
            if (!isScheduled)
            {
                var lines = load.Problems.Concat(sanitizeWarnings);
                MessageBox.Show(
                    $"部分数据异常，已按可运行值处理（磁盘现场已尽量备份）：\n\n{string.Join("\n", lines)}",
                    "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        log.Info($"数据目录：{DataDir}，日志目录：{logDir}，退出标记存在：{store.HasExitMarker()}");

        // 主题必须在任何窗口创建前应用：窗口 XAML 里的 StaticResource 会在此处已刷新的画刷上解析
        try
        {
            ThemeService.Apply(data.Settings);
            var accentName = ThemeAccentCatalog.Resolve(data.Settings.ThemeAccent).Name;
            log.Info($"主题已应用：{(data.Settings.ThemeMode == ThemeMode.Dark ? "深色" : "浅色")} · {accentName}");
        }
        catch (Exception ex)
        {
            log.Warn($"主题应用失败，本次使用默认外观：{ex.Message}");
        }

        // 每日滚动快照：同一天只写一次，保留少量，供误导入/误删除后人工取回。
        // 本次加载存在损坏/读取失败域时跳过：否则会把不完整数据固化成当天唯一快照，
        // 反而覆盖/挤掉之后正常启动应生成的备份。
        if (load.Problems.Count > 0)
        {
            log.Warn("数据加载存在异常，跳过本次每日快照以避免固化不完整数据");
        }
        else if (!DataBackupService.TryCreateDailySnapshot(DataDir, data, out var snapshotError))
        {
            log.Warn($"每日数据快照失败：{snapshotError}");
        }

        var clock = SystemClock.Instance;
        var engine = new ReminderEngine(clock);
        var planner = new OccurrencePlanner(clock);
        var guard = new TaskSchedulerGuard(Environment.ProcessPath ?? "QuietRemind", log);
        var hotkey = new GlobalHotkeyService();
        _services = new AppServices
        {
            Store = store,
            Data = data,
            Clock = clock,
            Engine = engine,
            Planner = planner,
            Log = log,
            Guard = guard,
            Hotkey = hotkey,
            DataDir = DataDir,
        };
        _services.PersistenceFailed += OnPersistenceFailed;

        // 快速新增热键：事件驱动、无常驻线程；注册失败仅记日志（可能被其他程序占用）
        hotkey.Pressed += ShowQuickAdd;
        if (data.Settings.QuickAddHotkeyEnabled)
        {
            if (hotkey.Register())
            {
                log.Info("快速新增快捷键已注册：Ctrl+Alt+N");
            }
            else
            {
                log.Warn(hotkey.LastError ?? "快速新增快捷键注册失败");
            }
        }

        // 退出标记语义：标记记录的登录会话 LUID 与当前进程一致 → 属于本次登录会话，守护触发不拉起；
        // 属于上次会话/开机（或旧格式无 LUID）→ 清除并恢复提醒。
        // 登录会话 LUID 在同会话的手动/计划任务/提权进程间一致，且注销重登、重启、快速启动重新登录后变化。
        if (store.HasExitMarker())
        {
            var marker = store.ReadExitMarker();
            var currentSession = UserSession.TryGetLogonSessionId();
            var markerInCurrentSession = marker.SessionId is { } markerSession
                && currentSession is { } current
                && string.Equals(markerSession, current, StringComparison.OrdinalIgnoreCase);

            if (isScheduled && markerInCurrentSession)
            {
                // 设置已关闭守护但仍残留任务时，即使本轮因退出标记跳过拉起，也把残留任务注销掉
                if (!data.Settings.GuardEnabled)
                {
                    guard.Unregister();
                }
                log.Info($"守护触发：退出标记属于本次登录会话（{marker.Time:yyyy-MM-dd HH:mm:ss}，会话 {marker.SessionId}），本轮不拉起");
                Shutdown();
                return;
            }

            store.ClearExitMarker();
            log.Info(isScheduled
                ? "退出标记属于上次登录会话/开机：已清除并恢复提醒"
                : "手动启动：已清除退出标记并恢复提醒");
        }

        // 进程守护注册/注销（需求 7.1，设置默认开启；失败不阻断本次运行）
        if (data.Settings.GuardEnabled)
        {
            if (!guard.EnsureRegistered())
            {
                log.Warn("计划任务注册失败，本次运行不受影响");
                if (!isScheduled)
                {
                    MessageBox.Show("开机自启与进程守护注册失败，本次运行不受影响，但重启后可能不会自动启动。\n请查看日志后重试。",
                        "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        else if (!guard.Unregister())
        {
            log.Warn("开机自启与进程守护已关闭，但计划任务注销失败");
        }

        // 提醒事件 → 弹窗；错过判定实时落盘（需求 8.2：错过判定属状态变更）。
        // 落盘失败不阻断提醒展示（必达优先），失败由 AppServices 统一记录并提示。
        engine.RemindersDue += (due, missed) =>
        {
            if (missed.Count > 0)
            {
                _services.PersistOccurrences();
            }
            ShowReminder(due, missed);
        };
        engine.PreAlertsDue += OnPreAlertsDue;

        // 系统事件（需求 4 章关机拦截 / 5 章睡眠错过）
        SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // 托盘先就绪：启动扫描期间的持久化失败也能通过托盘气泡提示
        CreateTray();

        // 启动序列：停用任务未收尾实例清理 → 补生成实例 → 落盘 → 错过扫描（需求 5.2）
        var startupToday = DateOnly.FromDateTime(clock.Now);
        _lastPlanDate = startupToday;
        var skipped = planner.SettleUnfinishedForDisabledTasks(data.Occurrences, data.Tasks);
        if (skipped > 0)
        {
            log.Info($"启动清理：停用任务未收尾实例置为已跳过 {skipped} 条");
        }
        var startupLastPlanned = data.Settings.LastPlannedDate;
        if (!TryHandleClockRollback(startupToday, ref startupLastPlanned))
        {
            planner.EnsureUpTo(data.Occurrences, data.Tasks, notBefore: startupLastPlanned);
            data.Settings.LastPlannedDate = startupToday;
        }
        // 启动落盘同时固化数据归一化修正（坏时刻/缺失设置等），保证下次启动读到的就是修正后的数据
        if (!_services.Persist() && !isScheduled)
        {
            MessageBox.Show("提醒数据保存失败，本次会话的提醒状态可能无法持久化。请检查磁盘权限或日志后重试。",
                "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        engine.ScanMissed(data.Occurrences, data.Tasks, clock.Now);
        // 启动补扫提前提醒：睡眠/关机期间错过的提前提醒窗口在本次启动补发一次气泡
        engine.ScanPreAlerts(data.Occurrences, data.Tasks, clock.Now, data.Settings.PreAlertMinutes);
        log.Info($"启动完成：任务 {data.Tasks.Count} 条，实例 {data.Occurrences.Count} 条");

        // 秒级轮询（需求 3.1）
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        // 手动启动显示主界面；计划任务启动静默进托盘（需求 7.2.2 / 5.2.5）
        if (!isScheduled)
        {
            ShowMainWindow();
        }
    }

    private void OnPersistenceFailed(string message)
    {
        // 非阻塞提示：提醒窗为 Topmost 全屏，普通弹窗可能被遮挡，托盘气泡更稳妥。
        // 30 秒内只提示一次，避免磁盘持续不可写时反复弹气泡；每次失败仍完整写日志。
        if ((DateTime.Now - _lastPersistenceWarning).TotalSeconds < 30)
        {
            return;
        }
        _lastPersistenceWarning = DateTime.Now;
        try
        {
            _tray?.ShowBalloonTip("QuietRemind", message,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
        }
        catch (Exception)
        {
            // 气泡失败不影响主流程，详细错误已写日志
        }
    }

    /// <summary>提前提醒：仅托盘气泡，绝不改变主屏全屏提醒的形态与时机。</summary>
    private void OnPreAlertsDue(IReadOnlyList<Occurrence> alerts)
    {
        if (_services is null || alerts.Count == 0)
        {
            return;
        }

        // 已提醒标记实时落盘，避免重启后重复气泡；落盘失败由 AppServices 统一提示
        _services.PersistOccurrences();
        var first = alerts[0];
        var text = alerts.Count == 1
            ? $"即将提醒：{TaskContent(first)}（{first.TriggerAt:HH:mm}）"
            : $"{alerts.Count} 条任务即将提醒（最近 {alerts.Min(o => o.TriggerAt):HH:mm}）";
        try
        {
            _tray?.ShowBalloonTip("QuietRemind", text,
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        catch (Exception)
        {
            // 气泡失败不影响提醒主流程
        }
        _services.Log.Info($"提前提醒：{text}");
    }

    private string TaskContent(Occurrence occurrence)
        => _services?.Data.Tasks.FirstOrDefault(t => t.Id == occurrence.TaskId)?.Content ?? "任务";

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_services is null)
        {
            return;
        }
        var data = _services.Data;
        // 睡眠/休眠唤醒后的首轮轮询：先做错过扫描，再执行常规触发，
        // 避免定时器 tick 先于 Resume 处理器把睡眠期间到点实例误当正常到点。
        if (_resumeScanRequired)
        {
            _resumeScanRequired = false;
            _services.Log.Info("唤醒后首轮轮询：先执行错过扫描");
            _services.Engine.ScanMissed(data.Occurrences, data.Tasks, _services.Clock.Now);
            _services.Engine.ScanPreAlerts(data.Occurrences, data.Tasks, _services.Clock.Now,
                data.Settings.PreAlertMinutes);
        }
        // 实例补齐仅在跨天时执行（新增实例只可能出现在新的一天；任务增删改路径已显式重建）
        var today = DateOnly.FromDateTime(_services.Clock.Now);
        if (today != _lastPlanDate)
        {
            var lastPlanned = data.Settings.LastPlannedDate;
            if (!TryHandleClockRollback(today, ref lastPlanned))
            {
                var added = _services.Planner.EnsureUpTo(data.Occurrences, data.Tasks, notBefore: lastPlanned);
                data.Settings.LastPlannedDate = today;
                _services.PersistSettings();
                if (added > 0)
                {
                    _services.PersistOccurrences();
                }
            }
            _lastPlanDate = today;
            // 跨天后主界面的“今日待处理/下次提醒”需要同步刷新
            _mainWindow?.RefreshRows();
        }
        _services.Engine.Poll(data.Occurrences, data.Tasks, data.Settings.PreAlertMinutes);

        // 单实例唤醒检查：重复手动启动时弹出主界面（并入 1s 轮询，无常驻后台线程）
        if (_singleInstance?.TryConsumeShowMainSignal() == true)
        {
            ShowMainWindow();
        }

        // 关机拦截取消后若暂存提醒展示失败，稍后重试（节流 5 秒）
        if (!_shutdownInterceptActive && _deferredReminders.Count > 0)
        {
            FlushDeferredReminders();
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

        if (_shutdownInterceptActive)
        {
            // 关机拦截对话框是模态的，叠加全屏提醒会盖住关机决策；先暂存，取消关机后再展示
            _deferredReminders.AddRange(entries);
            _services.Log.Info($"关机拦截进行中，暂存提醒 {entries.Count} 条");
            return;
        }

        ShowReminderEntries(entries);
    }

    private void ShowReminderEntries(List<(ReminderTask Task, Occurrence Occ)> entries)
    {
        if (_services is null || entries.Count == 0)
        {
            return;
        }

        var summary = string.Join("；", entries.Select(e => $"{e.Task.Content}({e.Occ.OriginalTriggerAt:MM-dd HH:mm})"));
        if (summary.Length > 2000)
        {
            summary = summary[..2000] + "…";
        }
        _services.Log.Info($"提醒展示：{entries.Count} 条 [{summary}]");

        // 已有提醒窗时追加条目，避免多个全屏提醒窗堆叠
        if (_reminderWindow is not null)
        {
            _reminderWindow.AddEntries(entries);
            _reminderWindow.Activate();
            return;
        }

        var vm = new ReminderWindowViewModel(_services, entries);
        var win = new ReminderWindow(vm);
        _reminderWindow = win;
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_reminderWindow, win))
            {
                _reminderWindow = null;
            }
            _mainWindow?.RefreshRows();
        };
        win.Show();
        win.Activate();
    }

    private void FlushDeferredReminders(bool force = false)
    {
        if (_deferredReminders.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (!force && now - _lastDeferredFlushTick < 5000)
        {
            return;
        }
        _lastDeferredFlushTick = now;

        // 先展示成功再清空；展示失败保留条目，由后续轮询重试，避免异常把用户取消关机的提醒吞掉
        var entries = _deferredReminders.ToList();
        try
        {
            ShowReminderEntries(entries);
            _deferredReminders.Clear();
        }
        catch (Exception ex)
        {
            _services?.Log.Error("暂存提醒展示失败，将稍后重试", ex);
        }
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

        // 同步模态：系统等待本进程回复 WM_QUERYENDSESSION 期间，用户可在此窗口处理。
        // 期间新到的提醒暂存，避免 Topmost 全屏窗盖住关机决策。
        var cancelShutdown = false;
        _shutdownInterceptActive = true;
        try
        {
            cancelShutdown = win.ShowDialog() == true;
            // 先写回取消结果：后续暂存提醒展示即使异常，也不能让用户已确认的“取消关机”失效
            e.Cancel = cancelShutdown;
            if (cancelShutdown)
            {
                _services.Log.Info("用户选择【取消关机】，本次关机已中止");
            }
            else
            {
                _services.Log.Info("用户选择【仍要关机】，放行关机流程");
            }
        }
        finally
        {
            _shutdownInterceptActive = false;
            if (cancelShutdown)
            {
                FlushDeferredReminders(force: true);
            }
            else if (_deferredReminders.Count > 0)
            {
                // 不清空：真正关机会随进程结束，下次启动由错过扫描补提醒；
                // 若关机随后被其他程序/系统取消，本会话下一轮轮询仍能把它们展示出来，避免静默丢失
                _services.Log.Info($"关机放行：暂存提醒 {_deferredReminders.Count} 条，关机被取消时将在本次会话补展示");
            }
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // SystemEvents 在专用后台线程触发：必须封送回 UI 线程，避免与轮询并发读写共享状态
        if (_services is null || _exiting)
        {
            return;
        }

        if (e.Mode == PowerModes.Suspend)
        {
            // 不停止轮询（避免 Resume 事件丢失后程序永久不再轮询）；
            // 只打标记，由唤醒后的首轮轮询先补一次错过扫描。
            _resumeScanRequired = true;
            _services.Log.Info("系统进入睡眠/休眠，标记唤醒后先执行错过扫描");
            return;
        }

        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _resumeScanRequired = true;
        try
        {
            Dispatcher.Invoke(() =>
            {
                _services?.Log.Info("系统从睡眠/休眠唤醒，执行错过扫描");
                _services?.Engine.ScanMissed(_services.Data.Occurrences, _services.Data.Tasks, _services.Clock.Now);
                _services?.Engine.ScanPreAlerts(_services.Data.Occurrences, _services.Data.Tasks,
                    _services.Clock.Now, _services.Data.Settings.PreAlertMinutes);
                _resumeScanRequired = false;
            });
        }
        catch (Exception ex)
        {
            _services?.Log.Error("唤醒处理失败，首轮轮询将补做错过扫描", ex);
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
        // 从最小化状态呼出时必须先还原：WPF 的 WindowState 可能仍是 Normal（例如 Win+向下箭头
        // 或系统级最小化），仅 Show/Activate 只会闪烁任务栏。用 Win32 SW_RESTORE 兜底。
        var handle = new WindowInteropHelper(_mainWindow).Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SwRestore);
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Show();
        _mainWindow.Activate();
        _services.Log.Info($"呼出主界面（WindowState={_mainWindow.WindowState}）");
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Ctrl+Alt+N：轻量新增窗（单次/每天），保存走现有规划器与落盘链路。</summary>
    public void ShowQuickAdd()
    {
        if (_services is null)
        {
            return;
        }
        // 强制收尾的全屏提醒优先：避免快速新增窗被 Topmost 提醒窗盖住
        if (_reminderWindow is not null)
        {
            _reminderWindow.Activate();
            return;
        }
        if (_quickAddWindow is not null)
        {
            _quickAddWindow.Activate();
            return;
        }

        var vm = new QuickAddViewModel(_services.Clock.Now);
        var win = new QuickAddWindow(vm);
        // 主窗口最小化到任务栏时不设 Owner，避免对话框被最小化窗口带入后台
        if (_mainWindow is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            win.Owner = _mainWindow;
        }
        else
        {
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _quickAddWindow = win;
        try
        {
            if (win.ShowDialog() == true && vm.SavedTask is { } task)
            {
                AddQuickTask(task);
            }
        }
        finally
        {
            _quickAddWindow = null;
        }
    }

    private void AddQuickTask(ReminderTask task)
    {
        if (_services is null)
        {
            return;
        }
        _services.Data.Tasks.Add(task);
        // 与主界面新增保持一致：按新任务规则重建未来实例（已收尾历史不受影响）
        _services.Planner.RebuildFuture(_services.Data.Occurrences, task,
            notBefore: _services.Data.Settings.LastPlannedDate);
        _services.Persist();
        _services.Log.Info($"快速新增任务：{task.Content}（{task.RecurrenceType} {task.Time.ToString(@"hh\:mm")}）");
        _mainWindow?.RefreshRows();
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
            // 写入用户主动退出标记（含当前登录会话 LUID）后退出（需求 7.4）
            var sessionId = UserSession.TryGetLogonSessionId();
            _services.Store.WriteExitMarker(sessionId);
            _services.Log.Info($"用户确认退出，已写入主动退出标记（会话 {sessionId ?? "未知"}）");
            if (sessionId is null)
            {
                _services.Log.Warn("未获取到登录会话 LUID，守护可能在约 5 分钟后重新拉起本程序");
            }
        }
        catch (Exception ex)
        {
            // 标记写入失败也必须允许退出，否则用户点退出无响应（需求 9.3）
            _services.Log.Error("退出标记写入失败", ex);
            MessageBox.Show("退出标记写入失败，进程守护可能在约 5 分钟后重新拉起本程序。\n\n" + ex.Message,
                "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ExitApplication();
    }

    private void ExitApplication()
    {
        _exiting = true;
        _pollTimer?.Stop();
        _tray?.Dispose();
        if (_services is not null)
        {
            _services.Hotkey.Dispose();
            _services.Log.Info("QuietRemind 退出");
            _services.Log.Dispose();
        }
        Shutdown();
    }

    /// <summary>
    /// 备份导入后重启：启动带 --restart-after=&lt;pid&gt; 的新实例，新实例等待本进程退出后再初始化。
    /// 不写退出标记，避免守护语义被误判。
    /// </summary>
    public void RestartForImport()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                throw new InvalidOperationException("无法定位当前程序文件");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--restart-after={Environment.ProcessId}",
                UseShellExecute = false,
            });
            _services?.Log.Info("备份导入完成，已启动新实例并准备退出当前进程");
        }
        catch (Exception ex)
        {
            _services?.Log.Error("导入后自动重启失败，请手动重启", ex);
            MessageBox.Show("备份已导入，请手动退出并重新启动 QuietRemind 以生效。\n\n" + ex.Message,
                "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ExitApplication();
    }

    private static void WaitForRestartPredecessor(string[] args)
    {
        const string prefix = "--restart-after=";
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (arg is null || !int.TryParse(arg[prefix.Length..], out var pid))
        {
            return;
        }

        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(8000);
        }
        catch (ArgumentException)
        {
            // 旧进程已退出：直接继续启动
        }
        catch (Exception)
        {
            // 等待失败不应阻断新实例启动（单实例检查仍会兜底）
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

    /// <summary>
    /// 进程收尾留痕：正常退出会留下 ExitCode，被外部强制结束/原生崩溃则不会。
    /// 用静态追加而不是 LogService，避免 ExitApplication 已释放日志时漏记。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        LogService.AppendEarly(ResolveLogDir(), $"应用退出：ExitCode={e.ApplicationExitCode}");
        ClearRunningFlag();
        base.OnExit(e);
    }

    /// <summary>主实例启动时写运行标记；若上次标记仍在，说明那次没有走正常退出路径。</summary>
    private static void MarkRunning(LogService log)
    {
        try
        {
            if (File.Exists(RunningFlagPath))
            {
                var previous = File.ReadAllText(RunningFlagPath).Trim();
                log.Warn($"上次运行未正常退出（PID={(previous.Length == 0 ? "未知" : previous)}）：可能被强制结束或原生崩溃；如反复出现可结合系统事件日志排查");
            }
            File.WriteAllText(RunningFlagPath, Environment.ProcessId.ToString());
        }
        catch (Exception ex)
        {
            log.Warn($"写入运行标记失败：{ex.Message}");
        }
    }

    private static void ClearRunningFlag()
    {
        try
        {
            File.Delete(RunningFlagPath);
        }
        catch (Exception)
        {
            // 删除失败只会导致下次启动多一条提示，不影响退出
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _services?.Log.Error("未处理异常（非 UI 线程）", ex);
        }
    }

    /// <summary>
    /// 处理系统日期回拨：可信范围内（≤7 天，与 7 天规划窗口一致）返回 true 表示本轮跳过补生成；
    /// 若上次规划日期远超当前（疑似历史时钟前跳残留），重置基准并返回 false 继续规划。
    /// </summary>
    private bool TryHandleClockRollback(DateOnly today, ref DateOnly? lastPlanned)
    {
        if (!OccurrencePlanner.IsClockRolledBack(today, lastPlanned))
        {
            return false;
        }

        if (OccurrencePlanner.IsUsableRollbackGuard(today, lastPlanned))
        {
            _services?.Log.Warn($"检测到系统日期回拨：{today:yyyy-MM-dd} < 上次计划 " +
                                $"{lastPlanned?.ToString("yyyy-MM-dd") ?? "未知"}，跳过实例补生成");
            return true;
        }

        _services?.Log.Warn($"上次计划日期 {lastPlanned?.ToString("yyyy-MM-dd") ?? "未知"} 远超当前 " +
                            $"{today:yyyy-MM-dd}，视为时钟前跳残留，重置规划基准");
        lastPlanned = today;
        return false;
    }

    /// <summary>首选 D:\logs\QuietRemind；不可创建/不可写时回退到 %AppData%\QuietRemind\logs。</summary>
    private static string ResolveLogDir()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            // 仅创建成功不代表可写（权限受限目录），做一次轻量写探针
            var probe = Path.Combine(LogDir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "1");
            try
            {
                File.Delete(probe);
            }
            catch (Exception)
            {
                // 探针删除失败不影响“目录可写”的判定
            }
            return LogDir;
        }
        catch (Exception)
        {
            // 无 D: 盘、权限受限等场景回退
        }

        var fallback = Path.Combine(DataDir, "logs");
        try
        {
            Directory.CreateDirectory(fallback);
        }
        catch (Exception)
        {
            // 由调用方日志写入失败时静默处理
        }
        return fallback;
    }

    /// <summary>单实例次实例在 LogService 创建前退出，用轻量追加保证“守护跳过原因”可回溯（需求 10.1）。</summary>
    private static void TryAppendEarlyLog(string logDir, string message, string level = "INFO")
        => LogService.AppendEarly(logDir, message, level);
}
