using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietRemind.Helpers;
using QuietRemind.Models;
using QuietRemind.Services;

namespace QuietRemind.ViewModels;

/// <summary>主窗口视图模型：任务列表 + 设置区（需求 6.1）。</summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        AddCommand = new RelayCommand(OnAdd);
        PickImageCommand = new RelayCommand(OnPickImage);
        ClearImageCommand = new RelayCommand(OnClearImage);
        RefreshDiagnosticsCommand = new RelayCommand(RefreshDiagnostics);
        RepairGuardCommand = new RelayCommand(OnRepairGuard);
        OpenLogCommand = new RelayCommand(OnOpenLog);
        PreviewReminderCommand = new RelayCommand(() => PreviewRequested?.Invoke());
        UndoDeleteCommand = new RelayCommand(UndoDelete);
        ExportBackupCommand = new RelayCommand(OnExportBackup);
        ImportBackupCommand = new RelayCommand(OnImportBackup);

        var settings = _services.Data.Settings;
        foreach (var info in ThemeAccentCatalog.All)
        {
            var option = new ThemeAccentOptionViewModel(
                info,
                string.Equals(info.Key, settings.ThemeAccent, StringComparison.Ordinal),
                OnSelectAccent);
            option.UpdatePreview(settings.ThemeMode);
            ThemeAccentOptions.Add(option);
        }
        _previewMode = settings.ThemeMode;
        RefreshReminderPreview();

        RefreshDiagnostics();
        Refresh();
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    /// <summary>可选主题色（设置面板的外观分组）。</summary>
    public ObservableCollection<ThemeAccentOptionViewModel> ThemeAccentOptions { get; } = [];

    /// <summary>浅色模式；与 IsDarkMode 成对绑定到分段控件，写入 false 时忽略。</summary>
    public bool IsLightMode
    {
        get => _services.Data.Settings.ThemeMode == ThemeMode.Light;
        set
        {
            if (value)
            {
                ApplyTheme(ThemeMode.Light, _services.Data.Settings.ThemeAccent);
            }
        }
    }

    public bool IsDarkMode
    {
        get => _services.Data.Settings.ThemeMode == ThemeMode.Dark;
        set
        {
            if (value)
            {
                ApplyTheme(ThemeMode.Dark, _services.Data.Settings.ThemeAccent);
            }
        }
    }

    private ThemeMode? _previewMode;

    private void OnSelectAccent(ThemeAccentOptionViewModel option)
        => ApplyTheme(_services.Data.Settings.ThemeMode, option.Key);

    /// <summary>
    /// 切换主题：先更新设置并立即改画刷（已打开窗口同步刷新），再落盘。
    /// 落盘失败由 AppServices 统一提示，界面保持已应用的主题，不退回旧配色。
    /// </summary>
    private void ApplyTheme(ThemeMode mode, string accentKey)
    {
        if (!ThemeService.ApplyAndPersist(_services, mode, accentKey))
        {
            return;
        }

        var normalized = _services.Data.Settings.ThemeAccent;
        OnPropertyChanged(nameof(IsLightMode));
        OnPropertyChanged(nameof(IsDarkMode));
        // 色点预览色只随明暗模式变化；换主题色时只移动选中环，避免多余的画刷重建
        var previewNeedsUpdate = _previewMode != mode;
        foreach (var option in ThemeAccentOptions)
        {
            if (previewNeedsUpdate)
            {
                option.UpdatePreview(mode);
            }
            option.IsCurrent = string.Equals(option.Key, normalized, StringComparison.Ordinal);
        }
        _previewMode = mode;
        _services.Log.Info($"主题切换：{(mode == ThemeMode.Dark ? "深色" : "浅色")} · {ThemeAccentCatalog.Resolve(normalized).Name}");
    }

    /// <summary>今日待处理实例数（Pending/Missed），列表标题旁胶囊展示。</summary>
    public string PendingCountText
    {
        get => _pendingCountText;
        set => SetProperty(ref _pendingCountText, value);
    }
    private string _pendingCountText = "0 项待处理";

    /// <summary>无任务时的空态提示。</summary>
    public bool HasNoTask
    {
        get => _hasNoTask;
        set => SetProperty(ref _hasNoTask, value);
    }
    private bool _hasNoTask;

    /// <summary>顶部日期标签（如 2026年11月24日 星期二）。</summary>
    public string TodayLabel
    {
        get => _todayLabel;
        private set => SetProperty(ref _todayLabel, value);
    }
    private string _todayLabel = "";

    /// <summary>今日待处理实例数（数字，供概览区大字展示）。</summary>
    public string TodayPendingNumber
    {
        get => _todayPendingNumber;
        private set => SetProperty(ref _todayPendingNumber, value);
    }
    private string _todayPendingNumber = "0";

    /// <summary>启用任务数（数字）。</summary>
    public string EnabledTaskCountText
    {
        get => _enabledTaskCountText;
        private set => SetProperty(ref _enabledTaskCountText, value);
    }
    private string _enabledTaskCountText = "0";

    /// <summary>列表底部计数文案。</summary>
    public string TaskCountText
    {
        get => _taskCountText;
        private set => SetProperty(ref _taskCountText, value);
    }
    private string _taskCountText = "共 0 个任务";

    /// <summary>下一次提醒时间（今天/明天/日期 + 时刻）。</summary>
    public string NextReminderTimeText
    {
        get => _nextReminderTimeText;
        private set => SetProperty(ref _nextReminderTimeText, value);
    }
    private string _nextReminderTimeText = "—";

    /// <summary>下一次提醒的任务内容。</summary>
    public string NextReminderTaskText
    {
        get => _nextReminderTaskText;
        private set => SetProperty(ref _nextReminderTaskText, value);
    }
    private string _nextReminderTaskText = "暂无后续提醒";

    /// <summary>请求打开新增编辑器（View 层订阅实现）。</summary>
    public event Action? AddRequested;

    /// <summary>请求打开编辑编辑器。</summary>
    public event Action<ReminderTask>? EditRequested;

    /// <summary>请求打开只读历史记录。</summary>
    public event Action<ReminderTask>? HistoryRequested;

    /// <summary>请求以主屏全屏样式预览提醒（不落盘）。</summary>
    public event Action? PreviewRequested;

    /// <summary>导入备份后请求重启以加载新数据（由 App 执行）。</summary>
    public event Action? RestartRequested;

    /// <summary>请求删除确认。</summary>
    public event Action<ReminderTask>? DeleteRequested;

    public RelayCommand AddCommand { get; }

    public RelayCommand PreviewReminderCommand { get; }

    public RelayCommand UndoDeleteCommand { get; }

    public RelayCommand ExportBackupCommand { get; }
    public RelayCommand ImportBackupCommand { get; }

    private void OnExportBackup()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 QuietRemind 备份",
            Filter = "QuietRemind 备份 (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"QuietRemind-backup-{DateTime.Now:yyyyMMdd-HHmm}.json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            DataBackupService.Export(dialog.FileName, _services.Data);
            _services.Log.Info($"备份已导出：{dialog.FileName}");
            System.Windows.MessageBox.Show($"备份已导出：\n{dialog.FileName}", "导出备份",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Log.Error("导出备份失败", ex);
            System.Windows.MessageBox.Show($"导出失败：{ex.Message}", "导出备份",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImportBackup()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 QuietRemind 备份",
            Filter = "QuietRemind 备份 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "导入将覆盖当前全部任务、设置和历史实例，且无法自动合并。\n导入前会自动备份当前数据。\n\n继续导入并重启程序？",
            "导入备份", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        string? preImport = null;
        var importedApplied = false;
        try
        {
            var imported = DataBackupService.Import(dialog.FileName, out var warnings);
            preImport = DataBackupService.CreatePreImportBackup(_services.DataDir, _services.Data);
            // 先整体接管内存数据再落盘：若三个文件只写成功一部分，后续任何保存也会基于导入数据，
            // 不会被旧内存状态回写覆盖（此前失败后继续运行时存在该风险）
            _services.Data.ReplaceWith(imported);
            importedApplied = true;

            // 三个域分别尝试写入，避免首个失败导致其余域完全没写入
            var failures = new List<string>();
            void TrySave(string what, Action save)
            {
                try
                {
                    save();
                }
                catch (Exception ex)
                {
                    failures.Add($"{what}：{ex.Message}");
                    _services.Log.Error($"导入写入{what}失败", ex);
                }
            }
            TrySave("任务", () => _services.Store.SaveTasks(imported.Tasks));
            TrySave("实例", () => _services.Store.SaveOccurrences(imported.Occurrences));
            TrySave("设置", () => _services.Store.SaveSettings(imported.Settings));

            if (failures.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    $"导入数据已载入内存，但部分文件写入失败：\n{string.Join("\n", failures)}\n\n" +
                    $"请重启程序后重试；若仍失败，可用导入前备份恢复：\n{preImport}",
                    "导入备份", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _services.Log.Info($"备份导入成功：{dialog.FileName}；导入前备份：{preImport}；归一化修正 {warnings.Count} 项");
            foreach (var warning in warnings)
            {
                _services.Log.Warn($"导入归一化：{warning}");
            }
            // 写入成功后立即重启，避免旧内存状态再次落盘覆盖导入数据
            RestartRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _services.Log.Error("导入备份失败", ex);
            var backupHint = preImport is null ? "" : $"\n\n导入前备份：{preImport}";
            var stateHint = importedApplied
                ? "\n\n当前会话已切换到导入数据，请重启程序确保提醒引擎重新加载。"
                : "\n\n当前内存数据未变。";
            System.Windows.MessageBox.Show(
                $"导入失败：{ex.Message}{stateHint}{backupHint}",
                "导入备份", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>删除撤销提示是否可见（主窗口底部 5 秒短提示）。</summary>
    public bool HasUndoDelete
    {
        get => _hasUndoDelete;
        private set => SetProperty(ref _hasUndoDelete, value);
    }
    private bool _hasUndoDelete;

    public string UndoDeleteText
    {
        get => _undoDeleteText;
        private set => SetProperty(ref _undoDeleteText, value);
    }
    private string _undoDeleteText = "";
    private ReminderTask? _deletedTask;
    private int _deletedTaskIndex;

    /// <summary>删除任务并保留一次撤销快照；历史实例不删除，撤销后重新参与提醒。</summary>
    public void DeleteTask(ReminderTask task)
    {
        var index = _services.Data.Tasks.IndexOf(task);
        if (index < 0)
        {
            return;
        }

        _deletedTask = task;
        _deletedTaskIndex = index;
        _services.Data.Tasks.RemoveAt(index);
        _services.Persist();
        _services.Log.Info($"删除任务：{task.Content}（历史实例保留，可撤销）");
        UndoDeleteText = $"已删除任务「{task.Content}」";
        HasUndoDelete = true;
        Refresh();
    }

    /// <summary>撤销最近一次删除：恢复到原位置，补齐未来实例，未收尾实例重新参与提醒。</summary>
    public void UndoDelete()
    {
        if (_deletedTask is not { } task)
        {
            return;
        }

        _deletedTask = null;
        HasUndoDelete = false;
        if (!_services.Data.Tasks.Contains(task))
        {
            _services.Data.Tasks.Insert(Math.Clamp(_deletedTaskIndex, 0, _services.Data.Tasks.Count), task);
        }
        _services.Planner.EnsureUpTo(_services.Data.Occurrences, _services.Data.Tasks,
            notBefore: _services.Data.Settings.LastPlannedDate);
        _services.PersistTasks();
        _services.Log.Info($"撤销删除任务：{task.Content}");
        Refresh();
    }

    /// <summary>撤销提示到时收起（任务保持删除）。</summary>
    public void DismissUndo()
    {
        _deletedTask = null;
        HasUndoDelete = false;
    }

    /// <summary>运行诊断摘要（计划任务状态），仅打开主界面或手动刷新时查询。</summary>
    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetProperty(ref _diagnosticsSummary, value);
    }
    private string _diagnosticsSummary = "";

    /// <summary>诊断详情：上次运行结果、日志目录、最近落盘错误。</summary>
    public string DiagnosticsDetail
    {
        get => _diagnosticsDetail;
        private set => SetProperty(ref _diagnosticsDetail, value);
    }
    private string _diagnosticsDetail = "";

    /// <summary>守护缺失/配置异常/落盘失败时以警示色展示。</summary>
    public bool HasDiagnosticsWarning
    {
        get => _hasDiagnosticsWarning;
        private set => SetProperty(ref _hasDiagnosticsWarning, value);
    }
    private bool _hasDiagnosticsWarning;

    public RelayCommand RefreshDiagnosticsCommand { get; }
    public RelayCommand RepairGuardCommand { get; }
    public RelayCommand OpenLogCommand { get; }

    private void RefreshDiagnostics()
    {
        var status = _services.Guard.GetStatus();
        var guardEnabled = _services.Data.Settings.GuardEnabled;
        var lines = new List<string>();
        if (status.TaskExists && status.LastRunTime is { } lastRun && lastRun.Year > 2000)
        {
            lines.Add($"上次运行：{lastRun:yyyy-MM-dd HH:mm}（结果码 {status.LastTaskResult?.ToString() ?? "未知"}）");
        }
        lines.Add($"日志目录：{_services.LogDir}");
        lines.Add(_services.LastPersistenceError is { } error
            ? $"最近落盘错误：{_services.LastPersistenceErrorAt:MM-dd HH:mm} {error}"
            : "本次会话暂无落盘错误");
        lines.Add(!_services.Data.Settings.QuickAddHotkeyEnabled
            ? "快速新增：快捷键已关闭"
            : _services.Hotkey.IsRegistered
                ? "快速新增：Ctrl+Alt+N 已启用"
                : $"快速新增：快捷键未注册（{_services.Hotkey.LastError ?? "未知原因"}）");

        // 守护关闭时不再把“未注册”标红；但计划任务残留仍提示（注销失败）
        DiagnosticsSummary = guardEnabled
            ? status.Summary
            : status.TaskExists
                ? "守护已关闭，但计划任务仍存在（注销未完成）"
                : "守护已关闭（计划任务已注销）";
        DiagnosticsDetail = string.Join("\n", lines);
        var guardProblem = guardEnabled
            ? !status.TaskExists || !status.Enabled || !status.ActionMatches
                || !status.HasGuardTrigger || !status.HasLogonTrigger
            : status.TaskExists;
        HasDiagnosticsWarning = guardProblem
            || _services.LastPersistenceError is not null
            || (_services.Data.Settings.QuickAddHotkeyEnabled && !_services.Hotkey.IsRegistered);
    }

    private void OnRepairGuard()
    {
        if (!_services.Data.Settings.GuardEnabled)
        {
            System.Windows.MessageBox.Show("开机自启与进程守护已关闭，请先开启上方开关再修复。", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_services.Guard.EnsureRegistered())
        {
            System.Windows.MessageBox.Show("计划任务修复失败，请查看日志后重试。", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshDiagnostics();
    }

    private void OnOpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _services.LogDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开日志目录：{ex.Message}", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public bool GuardEnabled
    {
        get => _services.Data.Settings.GuardEnabled;
        set
        {
            if (_services.Data.Settings.GuardEnabled == value)
            {
                return;
            }

            // 需求 6.3：关闭即注销计划任务，不再自启、不再守护；开启则（重新）注册。
            // 失败时不改变设置并回滚开关，避免界面状态与真实守护状态不一致。
            if (value)
            {
                if (!_services.Guard.EnsureRegistered())
                {
                    ShowGuardError("开机自启与进程守护注册失败，设置未生效。请查看日志后重试。");
                    OnPropertyChanged();
                    return;
                }
            }
            else if (!_services.Guard.Unregister())
            {
                ShowGuardError("计划任务注销失败，设置未生效。请查看日志后重试。");
                OnPropertyChanged();
                return;
            }

            _services.Data.Settings.GuardEnabled = value;
            _services.PersistSettings();
            OnPropertyChanged();
            RefreshDiagnostics();
        }
    }

    private static void ShowGuardError(string message)
        => MessageBox.Show(message, "QuietRemind", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>快速新增全局快捷键开关：注册失败不改变设置，并回滚界面状态。</summary>
    public bool QuickAddHotkeyEnabled
    {
        get => _services.Data.Settings.QuickAddHotkeyEnabled;
        set
        {
            if (_services.Data.Settings.QuickAddHotkeyEnabled == value)
            {
                return;
            }

            if (!_services.Hotkey.SetEnabled(value))
            {
                ShowGuardError("全局快捷键 Ctrl+Alt+N 注册失败，可能已被其他程序占用。");
                OnPropertyChanged();
                return;
            }

            _services.Data.Settings.QuickAddHotkeyEnabled = value;
            _services.PersistSettings();
            OnPropertyChanged();
            RefreshDiagnostics();
        }
    }

    public int Snooze1
    {
        get => GetSnooze(0);
        set => SetSnooze(0, value, nameof(Snooze1));
    }

    public int Snooze2
    {
        get => GetSnooze(1);
        set => SetSnooze(1, value, nameof(Snooze2));
    }

    public int Snooze3
    {
        get => GetSnooze(2);
        set => SetSnooze(2, value, nameof(Snooze3));
    }

    private int GetSnooze(int index)
    {
        var minutes = _services.Data.Settings.SnoozeMinutes;
        return minutes is not null && index < minutes.Length ? minutes[index] : 5;
    }

    private void SetSnooze(int index, int value, string propertyName)
    {
        var minutes = _services.Data.Settings.SnoozeMinutes;
        if (minutes is null || index >= minutes.Length)
        {
            return;
        }
        var clamped = Math.Clamp(value, 1, 120);
        if (minutes[index] != clamped)
        {
            minutes[index] = clamped;
            _services.PersistSettings();
        }
        // 即使值未变也通知：输入 0/999 时让文本框回显夹紧后的真实值
        OnPropertyChanged(propertyName);
    }

    /// <summary>提前提醒开关：关闭即写入 0；开启默认提前 5 分钟（仅托盘气泡）。</summary>
    public bool PreAlertEnabled
    {
        get => _services.Data.Settings.PreAlertMinutes > 0;
        set
        {
            if (value == PreAlertEnabled)
            {
                return;
            }
            _services.Data.Settings.PreAlertMinutes = value
                ? Math.Max(5, _services.Data.Settings.PreAlertMinutes)
                : 0;
            _services.PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreAlertMinutes));
        }
    }

    /// <summary>提前提醒分钟数（1~60，仅在开关开启时有意义）。</summary>
    public int PreAlertMinutes
    {
        get => _services.Data.Settings.PreAlertMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (_services.Data.Settings.PreAlertMinutes == clamped)
            {
                return;
            }
            _services.Data.Settings.PreAlertMinutes = clamped;
            _services.PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreAlertEnabled));
        }
    }

    /// <summary>提醒遮罩暗化不透明度（0~90%）。</summary>
    public int ReminderDimPercent
    {
        get => _services.Data.Settings.ReminderDimPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 90);
            if (_services.Data.Settings.ReminderDimPercent == clamped)
            {
                return;
            }
            _services.Data.Settings.ReminderDimPercent = clamped;
            _services.PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReminderDimOpacity));
        }
    }

    /// <summary>背景图片模糊半径（0~40px）。</summary>
    public int ReminderBlurRadius
    {
        get => _services.Data.Settings.ReminderBlurRadius;
        set
        {
            var clamped = Math.Clamp(value, 0, 40);
            if (_services.Data.Settings.ReminderBlurRadius == clamped)
            {
                return;
            }
            _services.Data.Settings.ReminderBlurRadius = clamped;
            _services.PersistSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>已设置的背景图展示名（未设置为提示文字）。</summary>
    public string ReminderImageText =>
        string.IsNullOrEmpty(_services.Data.Settings.ReminderImagePath)
            ? "未设置（使用默认深色底）"
            : Path.GetFileName(_services.Data.Settings.ReminderImagePath);

    public RelayCommand PickImageCommand { get; }
    public RelayCommand ClearImageCommand { get; }

    private void OnPickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择提醒背景图片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var source = new FileInfo(dialog.FileName);
            if (source.Length > 20 * 1024 * 1024)
            {
                System.Windows.MessageBox.Show("背景图片不能超过 20MB，请压缩后重试。", "QuietRemind",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 拷贝到数据目录统一管理，避免源文件移动/删除后背景失效
            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var target = Path.Combine(_services.DataDir, $"reminder-bg{ext}");
            var sourcePath = Path.GetFullPath(dialog.FileName);
            if (!string.Equals(sourcePath, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                // 先写临时文件再替换，成功后才清理旧图：避免拷贝失败时旧背景已被删除
                var temp = target + ".tmp";
                try
                {
                    File.Copy(sourcePath, temp, overwrite: true);
                    File.Move(temp, target, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
            }
            foreach (var stale in Directory.GetFiles(_services.DataDir, "reminder-bg.*"))
            {
                if (string.Equals(stale, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    File.Delete(stale);
                }
                catch (Exception)
                {
                    // 旧图被占用时保留即可，不能因此让新图设置失败
                }
            }
            _services.Data.Settings.ReminderImagePath = target;
            _services.PersistSettings();
            _services.Log.Info($"提醒背景图已设置：{target}");
        }
        catch (Exception ex)
        {
            _services.Log.Error("背景图设置失败", ex);
            System.Windows.MessageBox.Show($"背景图设置失败：{ex.Message}", "QuietRemind",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        OnPropertyChanged(nameof(ReminderImageText));
        OnPropertyChanged(nameof(HasCustomImage));
        RefreshReminderPreview();
    }

    private void OnClearImage()
    {
        try
        {
            var path = _services.Data.Settings.ReminderImagePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                // 只删除本程序托管在数据目录中的背景图；导入的备份可能指向用户自己的文件，
                // 清空设置不应误删数据目录之外的文件
                var full = Path.GetFullPath(path);
                var dataDir = Path.GetFullPath(_services.DataDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (full.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith("reminder-bg.", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(full);
                }
            }
        }
        catch (Exception)
        {
            // 删除失败不影响清除设置（残留文件不会覆盖新选择）
        }
        _services.Data.Settings.ReminderImagePath = null;
        _services.PersistSettings();
        _services.Log.Info("提醒背景图已清除");
        OnPropertyChanged(nameof(ReminderImageText));
        OnPropertyChanged(nameof(HasCustomImage));
        RefreshReminderPreview();
    }

    public bool HasCustomImage => !string.IsNullOrEmpty(_services.Data.Settings.ReminderImagePath);

    /// <summary>设置区实时预览用的背景缩略图（按 640px 宽解码，滑块拖动时重算模糊才跟得上）。</summary>
    public ImageSource? ReminderPreviewSource
    {
        get => _reminderPreviewSource;
        private set
        {
            if (SetProperty(ref _reminderPreviewSource, value))
            {
                OnPropertyChanged(nameof(HasReminderPreview));
            }
        }
    }
    private ImageSource? _reminderPreviewSource;

    public bool HasReminderPreview => _reminderPreviewSource is not null;

    /// <summary>预览层的不透明度：把 0~90% 换算成 0~0.9。</summary>
    public double ReminderDimOpacity => _services.Data.Settings.ReminderDimPercent / 100.0;

    private void RefreshReminderPreview()
    {
        var path = _services.Data.Settings.ReminderImagePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ReminderPreviewSource = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;          // 读完即释放文件，避免占用用户图片
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 640;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ReminderPreviewSource = bitmap;
        }
        catch (Exception ex)
        {
            _services.Log.Warn($"提醒预览图加载失败：{ex.Message}");
            ReminderPreviewSource = null;
        }
    }

    /// <summary>数据变更后重建任务行并刷新派生状态。</summary>
    public void Refresh()
    {
        var now = _services.Clock.Now;
        var today = DateOnly.FromDateTime(now);
        var todayPending = 0;
        ReminderTask? nextTask = null;
        var nextAt = DateTime.MaxValue;
        var hasOverdue = false;

        // 一次归集，避免每个任务都全量扫描实例历史
        var occurrencesByTask = _services.Data.Occurrences
            .GroupBy(o => o.TaskId)
            .ToDictionary(g => g.Key, g => g.ToList());

        Tasks.Clear();
        foreach (var task in _services.Data.Tasks)
        {
            var row = new TaskRowViewModel(task, OnTaskToggled,
                t => HistoryRequested?.Invoke(t),
                t => EditRequested?.Invoke(t),
                t => DeleteRequested?.Invoke(t));
            var taskOccs = occurrencesByTask.TryGetValue(task.Id, out var list) ? list : [];
            row.IsTodayDue = task.Enabled
                && taskOccs.Any(o => DateOnly.FromDateTime(o.TriggerAt) == today
                    && o.State is OccurrenceState.Pending or OccurrenceState.Missed);
            row.IsEnded = task.RecurrenceType == RecurrenceType.Once
                && taskOccs.Count > 0
                && taskOccs.All(o => o.State is OccurrenceState.Completed or OccurrenceState.Skipped);

            // 下一次未处理提醒时刻（列表“下次提醒”列）；停用/已结束给出明确状态，避免显示过期时刻
            var next = taskOccs
                .Where(o => o.State is OccurrenceState.Pending or OccurrenceState.Missed)
                .Select(o => o.TriggerAt)
                .OrderBy(t => t)
                .FirstOrDefault();
            row.NextRemindText = !task.Enabled
                ? "已停用"
                : row.IsEnded
                    ? "已结束"
                    : next == default
                        ? "—"
                        : DateOnly.FromDateTime(next) switch
                        {
                            var d when d == today => $"今天 {next:HH:mm}",
                            var d when d == today.AddDays(1) => $"明天 {next:HH:mm}",
                            var d => $"{d:M月d日} {next:HH:mm}",
                        };

            // 顶部概览取“未来最近一次”，过期未处理单独提示，避免把过去时刻展示为下一次
            if (task.Enabled)
            {
                foreach (var occurrence in taskOccs)
                {
                    if (occurrence.State is not (OccurrenceState.Pending or OccurrenceState.Missed))
                    {
                        continue;
                    }
                    if (occurrence.TriggerAt >= now)
                    {
                        if (occurrence.TriggerAt < nextAt)
                        {
                            nextAt = occurrence.TriggerAt;
                            nextTask = task;
                        }
                    }
                    else
                    {
                        hasOverdue = true;
                    }
                }
            }

            if (row.IsTodayDue)
            {
                todayPending++;
            }
            Tasks.Add(row);
        }

        PendingCountText = $"{todayPending} 项待处理";
        TodayLabel = now.ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));
        TodayPendingNumber = todayPending.ToString(CultureInfo.InvariantCulture);
        EnabledTaskCountText = _services.Data.Tasks.Count(t => t.Enabled).ToString(CultureInfo.InvariantCulture);
        TaskCountText = $"共 {Tasks.Count} 个任务";
        if (nextTask is not null)
        {
            NextReminderTimeText = FormatNextReminder(nextAt, today);
            NextReminderTaskText = nextTask.Content;
        }
        else if (hasOverdue)
        {
            NextReminderTimeText = "待处理";
            NextReminderTaskText = "有提醒等待处理";
        }
        else
        {
            NextReminderTimeText = "—";
            NextReminderTaskText = "暂无后续提醒";
        }
        HasNoTask = Tasks.Count == 0;
    }

    private static string FormatNextReminder(DateTime at, DateOnly today)
    {
        var date = DateOnly.FromDateTime(at);
        return date == today
            ? $"今天 {at:HH:mm}"
            : date == today.AddDays(1)
                ? $"明天 {at:HH:mm}"
                : $"{date:M月d日} {at:HH:mm}";
    }

    private void OnAdd() => AddRequested?.Invoke();

    /// <summary>启用/停用切换后：停用任务的未收尾实例收尾，启用任务补生成，随后刷新派生状态。</summary>
    private void OnTaskToggled()
    {
        var skipped = _services.Planner.SettleUnfinishedForDisabledTasks(
            _services.Data.Occurrences, _services.Data.Tasks);
        if (skipped > 0)
        {
            _services.Log.Info($"停用任务未收尾实例置为已跳过：{skipped} 条");
        }

        _services.Planner.EnsureUpTo(_services.Data.Occurrences, _services.Data.Tasks,
            notBefore: _services.Data.Settings.LastPlannedDate);
        _services.PersistTasks();

        // 切换本身来自行内绑定更新，延迟到本次更新结束后重建列表，避免集合在绑定过程中被清空
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Refresh));
    }
}
