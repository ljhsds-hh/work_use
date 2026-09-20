using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SpaceMaid.App.Helpers;
using SpaceMaid.App.Services;
using SpaceMaid.Core;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.App.ViewModels;

/// <summary>
/// 主窗的三个页面。纯界面状态：它不参与任何清理决策，也不进设置文件
/// （每次启动都回到"磁盘概览"，避免上次停在隔离区页就成了隐性状态）。
/// </summary>
public enum MainPage
{
    Overview,
    Clean,
    Quarantine
}

/// <summary>
/// 主界面 ViewModel（三段式：磁盘总览 / 分级清单 / 操作栏）。
///
/// 它刻意**不认识任何 WPF 控件**，也从不自己弹窗：对话框、文件夹选择、Growl、打开目录全部走接口，
/// 因此可以在没有 UI 线程的测试里跑完整流程（设计文档 §11 ViewModel 单测）。
///
/// 四条被单测钉死的行为：
/// ① 重新扫描后勾选状态重置为条目 DefaultChecked（需求 5.4-2）；
/// ② L3（以及任何不可还原动作）必须二次确认，取消则**不调用执行器**（需求 3.3-4 / 4.3-1）；
/// ③ 同卷一律说"已移入隔离区"，绝不说"已释放"（需求 3.2-6 / D-6，文案取自内核 VolumeTextFormatter）；
/// ④ 导出清单与执行是两个人工作动作，中间没有自动衔接（需求 3.9-3）。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly ICoreBridge _bridge;
    private readonly IScanService _scanner;
    private readonly ICleanExecutor _executor;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IShellService _shell;
    private readonly UiDispatcher _ui = UiDispatcher.Capture();

    private CancellationTokenSource? _scanCts;
    private AppSettingsView _settings;
    private StartupPreparation? _preparation;
    private ScanReport? _lastReport;
    private CleanPlan? _lastPlan;
    private ManifestRowIndex? _manifestIndex;

    private long _volumeTotalBytes;
    private long _volumeFreeBytes;
    private long _processableBytes;
    private long _checkedBytes;
    private int _checkedItemCount;
    private bool _isBusy;
    private bool _isScanning;
    private string _busyText = string.Empty;
    private string _progressText = string.Empty;
    private string _statusMessage = string.Empty;
    private string _startupStatusMessage = string.Empty;
    private string _blockReason = string.Empty;
    private bool _isFlowBlocked;
    private bool _hasExecutionResult;
    private bool _isExecuting;
    private string _resultSummaryText = string.Empty;
    private string _resultDetailText = string.Empty;
    private string _reviewHint = string.Empty;
    private string _reviewReportPath = string.Empty;
    private string _manifestDirectory = string.Empty;
    private CleanCategory _selectedCategory = CleanCategory.L1OneClick;
    private MainPage _activePage = MainPage.Overview;
    private string _quarantineUsageText = "尚未读取";
    private string _quarantineCountText = string.Empty;
    private string _quarantineExpiredText = string.Empty;

    public MainViewModel(
        ICoreBridge bridge,
        IScanService scanner,
        ICleanExecutor executor,
        IDialogService dialogs,
        IFolderPicker folderPicker,
        INotificationService notifications,
        IShellService shell)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _settings = bridge.Settings;

        RescanCommand = new RelayCommand(() => _ = RescanAsync());
        ExportManifestCommand = new RelayCommand(() => _ = ExportManifestAsync());
        OneClickCleanCommand = new RelayCommand(() => _ = OneClickCleanAsync());
        CleanSelectedCommand = new RelayCommand(() => _ = CleanSelectedAsync());
        CancelScanCommand = new RelayCommand(CancelScan);
        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
        RunReviewCommand = new RelayCommand(() => _ = RunReviewAsync());
        OpenReportDirectoryCommand = new RelayCommand(() => _shell.OpenDirectory(ReportRoot));
        SelectCategoryCommand = new RelayCommand<CleanCategory>(category => SelectedCategory = category);
        ToggleAllCurrentCategoryCommand = new RelayCommand(ToggleAllCurrentCategory);
        OpenCategoryCommand = new RelayCommand<CleanCategory>(OpenCategory);
        ToggleCategoryAllCommand = new RelayCommand<CleanCategory>(category =>
        {
            SelectedCategory = category;
            ToggleAllCurrentCategory();
        });
        NavigateCommand = new RelayCommand<string>(Navigate);
        RefreshQuarantineCommand = new RelayCommand(RefreshQuarantine);
    }

    // ── 左侧导航（纯界面状态；不参与任何清理决策，也不记忆到设置里）──

    /// <summary>导航到某一页；参数 "settings" 是"打开设置窗口"而不是切页。</summary>
    public void Navigate(string? page)
    {
        switch (page)
        {
            case "overview":
                ActivePage = MainPage.Overview;
                break;
            case "clean":
                ActivePage = MainPage.Clean;
                break;
            case "quarantine":
                ActivePage = MainPage.Quarantine;
                RefreshQuarantine();
                break;
            case "settings":
                RequestOpenSettings?.Invoke();
                break;
        }
    }

    public MainPage ActivePage
    {
        get => _activePage;
        set
        {
            if (_activePage == value)
            {
                return;
            }

            _activePage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOverviewActive));
            OnPropertyChanged(nameof(IsCleanActive));
            OnPropertyChanged(nameof(IsQuarantineActive));
            OnPropertyChanged(nameof(ActivePageTitle));
            OnPropertyChanged(nameof(ActivePageSubtitle));
        }
    }

    // 三个 RadioButton 的选中态：只在"被选中"时导航（取消选中由同组其它项触发，不需要响应）
    public bool IsOverviewActive
    {
        get => ActivePage == MainPage.Overview;
        set { if (value) { ActivePage = MainPage.Overview; } }
    }

    public bool IsCleanActive
    {
        get => ActivePage == MainPage.Clean;
        set { if (value) { ActivePage = MainPage.Clean; } }
    }

    public bool IsQuarantineActive
    {
        get => ActivePage == MainPage.Quarantine;
        set { if (value) { ActivePage = MainPage.Quarantine; RefreshQuarantine(); } }
    }

    public string ActivePageTitle => ActivePage switch
    {
        MainPage.Clean => "清理计划",
        MainPage.Quarantine => "隔离区",
        _ => "磁盘概览"
    };

    public string ActivePageSubtitle => ActivePage switch
    {
        MainPage.Clean => "按分级审阅清理项；导出清单与执行是两步，导出不会动任何文件",
        MainPage.Quarantine => "所有被清理的文件都先放在这里，保留期内可以还原",
        _ => "先盘点、再决定；扫描只读，清理前一定先看清单"
    };

    // ── 隔离区页（需求 2.1 / 3.4-6：逐批次还原）──

    public ObservableCollection<QuarantineBatchViewModel> QuarantineBatches { get; } = new();

    public bool HasQuarantineBatches => QuarantineBatches.Count > 0;

    public string QuarantineUsageText
    {
        get => _quarantineUsageText;
        private set => SetProperty(ref _quarantineUsageText, value);
    }

    public string QuarantineCountText
    {
        get => _quarantineCountText;
        private set => SetProperty(ref _quarantineCountText, value);
    }

    public string QuarantineExpiredText
    {
        get => _quarantineExpiredText;
        private set => SetProperty(ref _quarantineExpiredText, value);
    }

    /// <summary>
    /// 重新读取隔离区现状并重建批次列表。**只读操作**：只调 <c>InspectQuarantine</c>，
    /// 不触发到期释放、不删任何东西——释放只发生在启动与扫描前（需求 3.4-5 的惰性语义）。
    /// 读不到（隔离区不可用）时把原因写进文案，不抛异常打断界面。
    /// </summary>
    public void RefreshQuarantine()
    {
        try
        {
            var info = _bridge.InspectQuarantine();

            QuarantineUsageText = $"{VolumeTextFormatter.FormatBytes(info.TotalBytes)}（{info.Batches.Count} 个批次）";
            QuarantineCountText = info.Batches.Count == 0 ? "隔离区是空的" : $"{info.Batches.Count} 个批次";
            QuarantineExpiredText = info.ExpiredBatchCount > 0
                ? $"其中 {info.ExpiredBatchCount} 个已到期，可在设置里立即清空"
                : "没有已到期的批次";

            QuarantineBatches.Clear();
            foreach (var batch in info.Batches.OrderByDescending(b => b.CreatedAt))
            {
                QuarantineBatches.Add(new QuarantineBatchViewModel(
                    batch,
                    id => _bridge.RestoreBatch(id),
                    _ => RefreshQuarantine())
                {
                    // 还原会把文件搬回原位，属于"改状态"的动作，点之前先让用户确认一次
                    Confirm = () => _dialogs.Confirm(
                        $"将把这个批次里的 {batch.EntryCount} 个文件"
                        + $"（{VolumeTextFormatter.FormatBytes(batch.TotalBytes)}）搬回它们原来的位置。"
                        + (batch.Expired ? Environment.NewLine + Environment.NewLine + "注意：该批次保留期已过，随时可能被自动释放。" : string.Empty)
                        + Environment.NewLine + Environment.NewLine
                        + "如果原位置已有同名文件，那个文件会被跳过、不会被覆盖。确定要还原吗？",
                        "还原隔离批次")
                });
            }
        }
        catch (Exception ex)
        {
            QuarantineUsageText = "隔离区当前不可读取";
            QuarantineCountText = ex.Message;
            QuarantineExpiredText = string.Empty;
            QuarantineBatches.Clear();
        }
        finally
        {
            OnPropertyChanged(nameof(HasQuarantineBatches));
        }
    }

    /// <summary>请求打开设置窗口（由 View 订阅，ViewModel 不 new 窗口）。</summary>
    public event Action? RequestOpenSettings;

    /// <summary>内核桥接：设置窗口复用同一套内核入口（View 只做转发）。</summary>
    public ICoreBridge Bridge => _bridge;

    /// <summary>用户请求确认时触发（顺序断言用：确认必须发生在执行之前）。</summary>
    public event Action<string>? ConfirmRequested;

    public ICommand RescanCommand { get; }

    public ICommand ExportManifestCommand { get; }

    public ICommand OneClickCleanCommand { get; }

    public ICommand CleanSelectedCommand { get; }

    public ICommand CancelScanCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand RunReviewCommand { get; }

    public ICommand OpenReportDirectoryCommand { get; }

    public ICommand SelectCategoryCommand { get; }

    public ICommand ToggleAllCurrentCategoryCommand { get; }

    /// <summary>左侧导航：参数 "overview" / "clean" / "quarantine" / "settings"。</summary>
    public ICommand NavigateCommand { get; }

    /// <summary>概览页的"查看该分级"：选中分级并切到清理计划页。</summary>
    public ICommand OpenCategoryCommand { get; }

    /// <summary>分级卡片/分组头里的"本分级全选/反选"：先选中该分级再切换勾选。</summary>
    public ICommand ToggleCategoryAllCommand { get; }

    /// <summary>隔离区页：重新读取现状与批次列表（只读）。</summary>
    public ICommand RefreshQuarantineCommand { get; }

    /// <summary>
    /// "一键清理（L1）"是否可点：不等于 <see cref="CanExecuteClean"/>——L1 没有勾选框，
    /// 所以不能用"已勾选 N 项"当门槛，只用"不忙 + 未被提权闸门挡住"。
    /// </summary>
    public bool CanOneClickClean => !IsBusy && !IsFlowBlocked;

    private void OpenCategory(CleanCategory category)
    {
        SelectedCategory = category;
        ActivePage = MainPage.Clean;
    }

    // ── ① 顶部：磁盘总览 ──

    public long VolumeTotalBytes
    {
        get => _volumeTotalBytes;
        private set
        {
            if (SetProperty(ref _volumeTotalBytes, value))
            {
                OnPropertyChanged(nameof(VolumeTotalText));
                OnPropertyChanged(nameof(VolumeUsedText));
                OnPropertyChanged(nameof(VolumeFreeText));
                OnPropertyChanged(nameof(UsedPercent));
                OnPropertyChanged(nameof(DiskHeadline));
            }
        }
    }

    public long VolumeFreeBytes
    {
        get => _volumeFreeBytes;
        private set
        {
            if (SetProperty(ref _volumeFreeBytes, value))
            {
                OnPropertyChanged(nameof(VolumeFreeText));
                OnPropertyChanged(nameof(VolumeUsedBytes));
                OnPropertyChanged(nameof(VolumeUsedText));
                OnPropertyChanged(nameof(UsedPercent));
                OnPropertyChanged(nameof(DiskHeadline));
            }
        }
    }

    public long VolumeUsedBytes => Math.Max(0, VolumeTotalBytes - VolumeFreeBytes);

    public string VolumeTotalText => FormatBytes(VolumeTotalBytes);

    public string VolumeUsedText => FormatBytes(VolumeUsedBytes);

    public string VolumeFreeText => FormatBytes(VolumeFreeBytes);

    /// <summary>已用占比（0–1）。</summary>
    public double UsedPercent => VolumeTotalBytes <= 0 ? 0 : Math.Round((double)VolumeUsedBytes / VolumeTotalBytes, 4);

    /// <summary>已用占比（0–100），给 hc:CircleProgressBar（Maximum 默认 100）用。</summary>
    public double UsedPercent100 => Math.Round(UsedPercent * 100, 2);

    public string UsedPercentText => $"{UsedPercent100:0.#}%";

    public string DiskHeadline => VolumeTotalBytes <= 0
        ? "还没有扫描结果"
        : $"{SystemDriveDisplay} 共 {VolumeTotalText}，已用 {VolumeUsedText}，可用 {VolumeFreeText}";

    /// <summary>本次可处理总量（清单里所有可执行项的体积之和）。</summary>
    public long ProcessableBytes
    {
        get => _processableBytes;
        private set
        {
            if (SetProperty(ref _processableBytes, value))
            {
                OnPropertyChanged(nameof(ProcessableText));
                OnPropertyChanged(nameof(TotalProcessableText));
            }
        }
    }

    public string ProcessableText => FormatBytes(ProcessableBytes);

    /// <summary>
    /// 需求 3.1-4 的汇总口径：同时给出"本次可处理"与"实际效果"，
    /// 同卷时必须说"已移入隔离区…（保留期结束或清空隔离区后释放）"（D-6）。
    /// </summary>
    public string TotalProcessableText =>
        ProcessableBytes <= 0
            ? "本次可处理 0 B"
            : $"本次可处理 {ProcessableText}；{DescribeEffect(ProcessableBytes)}";

    public string ProcessableDetail =>
        ProcessableBytes <= 0
            ? "扫描完成后这里会显示本次可处理的体积与落地口径。"
            : $"本次可处理 {ProcessableText}；{DescribeEffect(ProcessableBytes)}";

    // ── ② 中部：分级清单 ──

    public ObservableCollection<CleanItemViewModel> Items { get; } = new();

    public ObservableCollection<CleanSectionViewModel> Sections { get; } = new();

    public CleanCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(SelectedCategoryTitle));
                OnPropertyChanged(nameof(SelectedCategoryCheckableCount));
            }
        }
    }

    public string SelectedCategoryTitle => CategoryText(SelectedCategory);

    public int SelectedCategoryCheckableCount =>
        Items.Count(i => i.Category == SelectedCategory && i.ShowCheckBox);

    // ── 状态 ──

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanExecuteClean));
                OnPropertyChanged(nameof(CanExportManifest));
                OnPropertyChanged(nameof(CanOneClickClean));
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsNotScanning));
            }
        }
    }

    /// <summary>"可以中断扫描"的界面条件。</summary>
    public bool IsNotScanning => !IsScanning;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set => SetProperty(ref _isExecuting, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>启动自检结论（含隔离区状态消息，需求 3.6 + 隔离区可用性）。</summary>
    public string StartupStatusMessage
    {
        get => _startupStatusMessage;
        private set => SetProperty(ref _startupStatusMessage, value);
    }

    public bool IsElevated { get; private set; }

    /// <summary>不具备管理员权限时阻止进入清理流程（需求 3.6-2）。</summary>
    public bool IsFlowBlocked
    {
        get => _isFlowBlocked;
        private set
        {
            if (SetProperty(ref _isFlowBlocked, value))
            {
                OnPropertyChanged(nameof(CanExecuteClean));
            }
        }
    }

    public string BlockReason
    {
        get => _blockReason;
        private set => SetProperty(ref _blockReason, value);
    }

    public bool HasScanResult => _lastReport is not null;

    public bool CanStartScan => !IsBusy;

    public bool CanExportManifest => !IsBusy && _lastReport is not null;

    public bool CanExecuteClean => !IsBusy && !IsFlowBlocked && CheckedCount > 0;

    public int CheckedCount
    {
        get => _checkedItemCount;
        private set
        {
            if (SetProperty(ref _checkedItemCount, value))
            {
                OnPropertyChanged(nameof(CanExecuteClean));
                OnPropertyChanged(nameof(CheckedSummaryText));
                OnPropertyChanged(nameof(TotalProcessableText));
            }
        }
    }

    public long CheckedBytes
    {
        get => _checkedBytes;
        private set
        {
            if (SetProperty(ref _checkedBytes, value))
            {
                OnPropertyChanged(nameof(CheckedSummaryText));
                OnPropertyChanged(nameof(TotalProcessableText));
            }
        }
    }

    public string CheckedSummaryText =>
        CheckedCount == 0
            ? "还没有勾选任何项"
            : $"已勾选 {CheckedCount} 项，合计 {FormatBytes(CheckedBytes)}";

    // ── 同卷口径 ──

    /// <summary>清理计划是否与源文件同卷（内核给出，界面不自行判断卷）。</summary>
    public bool IsSameVolume => _lastPlan?.SameVolumeAsSource ?? true;

    /// <summary>
    /// 需求 3.2-6 / 3.4-7 的界面提示：同卷时**不得**出现"已释放"。
    /// 两句话的落地效果都直接引用内核 <see cref="VolumeTextFormatter.DescribeProcessed"/>，界面不自己拼口径。
    /// </summary>
    public string SameVolumeNotice => IsSameVolume
        ? $"隔离区在 {_bridge.QuarantineRoot}，与 C 盘同卷：文件会{DescribeSample()}，" +
          "C 盘空间不会立刻下降；建议在设置里把隔离区改到非系统盘。"
        : $"隔离区在 {_bridge.QuarantineRoot}（非系统盘）：文件移出后会{DescribeSample()}，C 盘空间会随之下降。";

    public string QuarantineRoot => _bridge.QuarantineRoot;

    public string ReportRoot => _settings.ReportDirectory;

    // ── ③ 执行结果 ──

    public bool HasExecutionResult
    {
        get => _hasExecutionResult;
        private set => SetProperty(ref _hasExecutionResult, value);
    }

    public string ResultSummaryText
    {
        get => _resultSummaryText;
        private set => SetProperty(ref _resultSummaryText, value);
    }

    public string ResultDetailText
    {
        get => _resultDetailText;
        private set => SetProperty(ref _resultDetailText, value);
    }

    /// <summary>复核入口的说明（需求 3.9-4：复核报告才是"审核完成"的依据）。</summary>
    public string ReviewHint
    {
        get => _reviewHint;
        private set => SetProperty(ref _reviewHint, value);
    }

    public string ReviewReportPath
    {
        get => _reviewReportPath;
        private set
        {
            if (SetProperty(ref _reviewReportPath, value))
            {
                OnPropertyChanged(nameof(HasReviewReport));
            }
        }
    }

    public bool HasReviewReport => !string.IsNullOrWhiteSpace(ReviewReportPath);

    public string ManifestDirectory => _manifestDirectory;

    /// <summary>
    /// 当前复核基准清单的行数（= 最近一次导出或执行的清单）。
    /// 用途：执行时与本次执行的集合比对，不一致就在状态栏明确提示——绝不悄悄换掉用户审阅过的基准。
    /// </summary>
    public int ExportedManifestRowCount => _manifestIndex?.Rows.Count ?? 0;

    // ── 启动编排 ──

    /// <summary>
    /// 启动自检（需求 3.6 / 设计文档 §6.4）：管理员自检、隔离区账本自检、到期惰性释放、日志滚动。
    /// 必须在界面出现之前完成准备工作，但**不阻塞**窗口出现（由 App 决定何时 await）。
    /// </summary>
    public StartupPreparation Initialize()
    {
        _settings = _bridge.Settings;
        IsElevated = _bridge.IsElevated;

        if (!IsElevated)
        {
            IsFlowBlocked = true;
            OnPropertyChanged(nameof(CanOneClickClean));
            BlockReason = "当前进程不具备管理员权限，已阻止进入清理流程。请通过 SpaceMaid.exe 启动（清单已固定要求管理员权限）。";
            _dialogs.Warn(BlockReason, "权限不足");
        }

        var preparation = _bridge.Prepare();
        _preparation = preparation;

        // 需求 3.4-5 要求"界面在启动时提示：隔离区有 X GB 已到期，可释放"。
        // 本工具无常驻进程（2.1），所以"到期释放"只能落在启动这一步的惰性释放里——
        // 释放完再查就查不到了，于是释放后立刻回读一次，区分两种结果并如实播报：
        //   ① 放掉了      -> "有 X 已到期，本次已自动释放 N 个批次"
        //   ② 没放掉      -> "仍有 X 已到期但未释放"（文件被占用/权限不足），并指出处理入口
        // 读不到隔离区现状不算错误：它只是提示，不能反过来打断启动。
        var expiredAfter = TryInspectQuarantine();

        var lines = new List<string>();
        if (preparation.Recovery.StoredRecovered > 0 || preparation.Recovery.MarkedUnknown > 0 || preparation.Recovery.PendingCleared > 0)
        {
            lines.Add($"隔离区账本自检：补记 {preparation.Recovery.StoredRecovered} 条、异常 {preparation.Recovery.MarkedUnknown} 条、清理待定 {preparation.Recovery.PendingCleared} 条");
        }

        var expiredNotice = DescribeExpiredQuarantine(preparation.Release, expiredAfter);
        if (expiredNotice is not null)
        {
            lines.Add(expiredNotice);
        }

        lines.Add(preparation.QuarantineMessage);
        StartupStatusMessage = MessageText.Join(lines);
        StatusMessage = StartupStatusMessage;

        if (expiredNotice is not null || preparation.Recovery.MarkedUnknown > 0)
        {
            _notifications.Notify(StartupStatusMessage);
        }

        OnPropertyChanged(nameof(QuarantineRoot));
        OnPropertyChanged(nameof(ReportRoot));
        OnPropertyChanged(nameof(SameVolumeNotice));

        // 概览页要显示隔离区占用，启动时就先读一次（只读，不会触发任何释放/删除）
        RefreshQuarantine();

        return preparation;
    }

    private QuarantineInfo? TryInspectQuarantine()
    {
        try
        {
            return _bridge.InspectQuarantine();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 生成"隔离区到期"的启动提示（需求 3.4-5）。没有任何到期批次时返回 <c>null</c>（不打扰用户）。
    ///
    /// 两种措辞严格区分，不许混用：**放掉了**才可以说"已自动释放"；**没放掉**只能说"仍有已到期但未释放"。
    /// 后者是真实会发生的（隔离区文件被占用、权限不足），这时候说"已释放"就是谎报。
    /// </summary>
    public static string? DescribeExpiredQuarantine(ReleaseResult release, QuarantineInfo? afterRelease)
    {
        var leftoverBytes = afterRelease?.Batches.Where(b => b.Expired).Sum(b => b.TotalBytes) ?? 0;
        if (afterRelease is { ExpiredBatchCount: > 0 })
        {
            return $"隔离区仍有 {FormatBytes(leftoverBytes)} 已到期但未释放（{afterRelease.ExpiredBatchCount} 个批次），可在设置页「立即清空隔离区」处理";
        }

        if (release.ReleasedBatches > 0)
        {
            return $"隔离区有 {FormatBytes(release.ReleasedBytes)} 已到期，本次已自动释放 {release.ReleasedBatches} 个批次";
        }

        return null;
    }

    /// <summary>兼容异步调用点（自检本身是同步的惰性工作）。</summary>
    public Task<StartupPreparation> InitializeAsync() => Task.FromResult(Initialize());

    // ── 扫描 ──

    /// <summary>
    /// 重新扫描。三条硬性质：
    /// ① 扫描前先做一次隔离区到期惰性释放（需求 3.4-5：无常驻进程，"定时"只能落在这里）；
    /// ② 只读，且全程 async，UI 不阻塞；
    /// ③ 扫描完成后勾选状态**重置为条目 DefaultChecked**（需求 5.4-2）。
    /// </summary>
    public async Task RescanAsync(bool includeRecycleBin = true)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsScanning = true;
        BusyText = "正在扫描…";
        ProgressText = "准备扫描…";

        var cts = new CancellationTokenSource();
        _scanCts = cts;

        try
        {
            _bridge.ReleaseExpired();

            var request = new ScanRequest(_bridge.Catalog, includeRecycleBin);            var progress = new Progress<ScanProgress>(p =>
                ProgressText = $"正在扫描：{p.DisplayName}（已统计 {FormatBytes(p.BytesSoFar)}，{p.FilesSoFar} 个文件）");

            var report = await _scanner.ScanAsync(request, progress, cts.Token).ConfigureAwait(true);

            _lastReport = report;
            UpdateVolumeOverview(report);
            ApplyReport(report);

            StatusMessage = $"扫描完成：共 {Items.Count} 项，可处理 {ProcessableText}（{report.Entries.Where(IsProcessable).Sum(e => (long)e.FileCount)} 个文件）。";
            ProgressText = StatusMessage;
            OnPropertyChanged(nameof(HasScanResult));
            OnPropertyChanged(nameof(CanExportManifest));
            OnPropertyChanged(nameof(SameVolumeNotice));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "扫描已取消。已扫描到的部分不会执行任何清理动作。";
            ProgressText = StatusMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
            ProgressText = StatusMessage;
            _notifications.Notify(StatusMessage);
        }
        finally
        {
            _scanCts = null;
            cts.Dispose();
            IsScanning = false;
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    public void CancelScan()
    {
        if (_scanCts is { IsCancellationRequested: false })
        {
            _scanCts.Cancel();
            ProgressText = "正在中断扫描…";
        }
    }

    /// <summary>把扫描报告转成界面条目与四个分级分组；勾选一律回到 DefaultChecked。</summary>
    private void ApplyReport(ScanReport report)
    {
        var plan = _bridge.BuildPlan(report, CheckedIdsFromItems());
        _lastPlan = plan;

        Items.Clear();
        foreach (var planItem in plan.Items)
        {
            var vm = new CleanItemViewModel(planItem, OnItemCheckedChanged);
            vm.ResetToDefault();
            Items.Add(vm);
        }

        RebuildSections();
        RefreshCheckedSummary();
        OnPropertyChanged(nameof(IsSameVolume));
        OnPropertyChanged(nameof(SameVolumeNotice));
        OnPropertyChanged(nameof(TotalProcessableText));
        OnPropertyChanged(nameof(ProcessableDetail));
    }

    private void RebuildSections()
    {
        var expanded = Sections
            .Where(s => s.IsExpanded)
            .Select(s => s.Category)
            .ToHashSet();

        Sections.Clear();
        foreach (var category in new[]
                 {
                     CleanCategory.L1OneClick,
                     CleanCategory.L2Recommended,
                     CleanCategory.L3Cautious,
                     CleanCategory.RecycleBin
                 })
        {
            var items = Items.Where(i => i.Category == category).ToList();
            Sections.Add(new CleanSectionViewModel(
                category,
                items,
                defaultExpanded: expanded.Count == 0 || expanded.Contains(category)));
        }
    }

    private void UpdateVolumeOverview(ScanReport report)
    {
        VolumeTotalBytes = report.Volume.TotalBytes;
        VolumeFreeBytes = report.Volume.FreeBytes;

        // "本次可处理"= 清单里所有**会执行**的条目的体积之和（含命令型动作，它们没有文件但有实际收益）。
        // 信息项（页面文件，需求 2.4）必须排除：它只展示体积、永不执行，算进来就是向用户承诺
        // 一件不会发生的事（真机 19 项里那一项就是 15 GB 的 C:\pagefile.sys，占了"可处理"总量的 70%）。
        ProcessableBytes = report.Entries
            .Where(IsProcessable)
            .Sum(e => e.TotalBytes);

        OnPropertyChanged(nameof(DiskHeadline));
        OnPropertyChanged(nameof(SameVolumeNotice));
    }

    /// <summary>
    /// 一个扫描条目是否计入"本次可处理"：
    /// ① 必须是可用项；② 有内容的文件型条目，或没有文件但有实际收益的命令型条目（休眠 / DISM）；
    /// ③ **排除信息项**——页面文件这类条目界面只展示体积、永不执行（需求 2.4）。
    /// 清单头部（<c>CleanPlan.PlannedBytes</c>）用的是同一判据，两边必须一致，否则界面与清单会各说一个数。
    /// </summary>
    private static bool IsProcessable(ScanEntry entry) =>
        entry.Available
        && entry.Item.ActionKind != CleanActionKind.InformationalOnly
        && (entry.HasContent || entry.Item.ActionKind is CleanActionKind.HibernateOff or CleanActionKind.DismComponentCleanup);

    private void OnItemCheckedChanged()
    {
        RefreshCheckedSummary();
        OnPropertyChanged(nameof(TotalProcessableText));
    }

    private void RefreshCheckedSummary()
    {
        // 信息项（页面文件，需求 2.4 明确"仅展示、不可清理"）不能计入"已勾选 N 项 / 合计 X"，
        // 否则确认框会承诺一个永远不会被清理的体积（对抗式评审 F-14）。
        var countable = Items.Where(i => i.IsChecked && !i.IsInformationalOnly).ToList();
        CheckedCount = countable.Count;
        CheckedBytes = countable.Sum(i => i.TotalBytes);
    }

    /// <summary>当前勾选项对应的 Id 集合（显式给出，避免"没勾任何项"被误当成"用默认"）。</summary>
    private HashSet<string> CheckedIdsFromItems() =>
        Items.Where(i => i.IsChecked).Select(i => i.ItemId).ToHashSet(StringComparer.Ordinal);

    /// <summary>需求 3.3-3：全选/反选只作用于当前分级，禁止"一键全选所有级别"。</summary>
    public void ToggleAllCurrentCategory()
    {
        var targets = Items.Where(i => i.Category == SelectedCategory && i.ShowCheckBox).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var target = !targets.All(i => i.IsChecked);
        foreach (var item in targets)
        {
            item.IsChecked = target;
        }

        RefreshCheckedSummary();
    }

    // ── 导出清单（需求 3.9-1：只读，不执行） ──

    public async Task ExportManifestAsync()
    {
        if (!CanExportManifest)
        {
            return;
        }

        IsBusy = true;
        BusyText = "正在导出清单…";

        try
        {
            var plan = BuildCurrentPlan();
            var paths = await Task.Run(() => _bridge.WriteManifest(plan)).ConfigureAwait(true);

            _manifestIndex = _bridge.BuildManifestIndex(plan, paths.Directory);
            _manifestDirectory = paths.Directory;

            StatusMessage = $"清单已导出：{paths.MarkdownPath}（csv：{paths.CsvPath}）。导出只写清单文件，不做任何删除或移动；" +
                            "请先人工审阅，再决定是否执行清理。";
            ProgressText = StatusMessage;
            ReviewHint = "执行完成后，点「查看复核报告」即可按这份清单逐条比对结果。";
            _notifications.Notify($"清单已导出到 {paths.Directory}");
            OnPropertyChanged(nameof(ManifestDirectory));
            OnPropertyChanged(nameof(ReviewHint));
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出清单失败：{ex.Message}";
            _notifications.Notify(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    // ── 执行 ──

    /// <summary>一键清理（L1，需求 3.2）：只处理 L1 分级，不看其它分级的勾选。</summary>
    public Task OneClickCleanAsync()
    {
        var l1 = Items.Where(i => i.Category == CleanCategory.L1OneClick).ToList();
        if (l1.Count == 0)
        {
            StatusMessage = "本次扫描没有任何 L1 一键直清项，无需执行。";
            return Task.CompletedTask;
        }

        return ExecuteAsync(l1, "一键清理（仅 L1 一键直清项）");
    }

    /// <summary>清理选中项（需求 3.3 / 3.9-3）：按勾选执行，执行前必须完成二次确认。</summary>
    public Task CleanSelectedAsync()
    {
        var checkedItems = Items.Where(i => i.IsChecked && !i.IsInformationalOnly).ToList();
        if (checkedItems.Count == 0)
        {
            StatusMessage = "还没有勾选任何项，请先在清单里选择；L1 项请用「一键清理（L1）」。";
            return Task.CompletedTask;
        }

        return ExecuteAsync(checkedItems, "清理勾选项");
    }

    /// <summary>
    /// 二次确认判定（抽成可测方法，需求 3.3-4 / 4.3-1）：
    /// L3 一律确认；任何分级的不可还原动作（关闭休眠、DISM）同样确认。
    /// 调用方只需传入**本次真的要执行**的项，判定本身不看勾选状态。
    /// </summary>
    public bool RequiresConfirmation(IEnumerable<CleanItemViewModel> items) =>
        items.Any(i => i.RequiresConfirmation);

    /// <summary>
    /// 本次勾选项里是否包含休眠项（决定要不要向用户要"关闭休眠功能"的授权，需求 3.8）。
    /// 界面用它把授权提示提前展示出来；真正传给执行器的值按"本次执行范围"单独算。
    /// </summary>
    public bool RequiresHibernateAuthorization =>
        Items.Any(i => i.IsChecked && i.RequiresHibernateAuthorization);

    private async Task ExecuteAsync(IReadOnlyList<CleanItemViewModel> scope, string actionName)
    {
        if (IsBusy || IsFlowBlocked)
        {
            return;
        }

        var checkedItems = scope.Where(i => i.IsChecked).ToList();
        if (checkedItems.Count == 0)
        {
            StatusMessage = "所选范围内没有勾选任何项。";
            return;
        }

        var needsConfirmation = RequiresConfirmation(checkedItems);
        var authorizeHibernate = checkedItems.Any(i => i.RequiresHibernateAuthorization);

        string? planId = null;
        ManifestRowIndex? reviewIndex = null;
        var reviewBasisChanged = false;

        if (needsConfirmation)
        {
            var message = BuildConfirmationText(actionName, checkedItems, authorizeHibernate);
            ConfirmRequested?.Invoke(message);

            if (!_dialogs.Confirm(message, $"{actionName} · 请确认"))
            {
                // 取消必须什么都不做：不构建计划、不调用执行器、不导出清单
                StatusMessage = "已取消：没有执行任何清理动作，也没有改动任何文件。";
                ProgressText = StatusMessage;
                return;
            }
        }

        IsBusy = true;
        IsExecuting = true;
        BusyText = $"{actionName}中…";
        ProgressText = BusyText;

        try
        {
            // 清单只包含本次要执行的项（D-7：清单即执行输入），因此按所选范围重新构建一次计划
            var plan = BuildPlanFor(checkedItems);
            planId = plan.PlanId;

            // ① 先把"本次真正要执行的集合"落成清单。复核必须与它严格对应——否则用户导出清单后又改了勾选，
            //    被搬走的文件不会出现在任何报告里，复核会给出"异常：0"的假结论（对抗式评审 F-4）。
            ManifestPaths executedPaths;
            try
            {
                executedPaths = await Task.Run(() => _bridge.WriteManifest(plan)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // 写不出凭据就**不执行**：没有清单的删除是不可复核的
                StatusMessage = $"无法写出本次执行的清单，已中止执行（没有清单的删除不可复核）：{ex.Message}";
                ProgressText = StatusMessage;
                _notifications.Notify(StatusMessage);
                return;
            }

            // ② 复核基准 = 本次执行的清单（而不是早先那份可能已经过期的导出）
            reviewIndex = _bridge.BuildManifestIndex(plan, executedPaths.Directory);
            // 这里比的是"清单行数"，所以必须用 ManifestFileCount（含信息项那一行）。
            // 用 PlannedFileCount 会因为信息项永远差一行，导致每次都误报"勾选被改动过"。
            reviewBasisChanged = _manifestIndex is not null && _manifestIndex.Rows.Count != plan.ManifestFileCount;
            _manifestIndex = reviewIndex;
            _manifestDirectory = executedPaths.Directory;

            var options = _bridge.BuildExecutionOptions(authorizeHibernate);
            var progress = new Progress<CleanProgress>(p =>
                ProgressText = $"正在处理：{p.DisplayName}（{p.ProcessedFiles}/{p.TotalFiles} 个文件，{FormatBytes(p.ProcessedBytes)}）");

            var report = await _executor.ExecuteAsync(plan, options, progress, CancellationToken.None).ConfigureAwait(true);

            ShowExecutionResult(report, plan);

            // ③ 按本次执行的清单复核；若与用户先前审阅的清单不一致，明确告知（绝不悄悄换基准）
            var review = _bridge.Review(reviewIndex, report.MovedBytes, report.SameVolume);
            ReviewReportPath = review.MarkdownPath;
            StatusMessage += reviewBasisChanged
                ? $" 注意：本次执行的集合与你先前导出审阅的清单**不一致**（勾选被改动过），复核报告以本次执行的清单为准。报告：{review.MarkdownPath}"
                : $" 复核报告已生成：{review.MarkdownPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"{actionName}失败：{ex.Message}";
            ProgressText = StatusMessage;
            _notifications.Notify(StatusMessage);
        }
        finally
        {
            IsExecuting = false;
            IsBusy = false;
            BusyText = string.Empty;
        }

        // 执行后重新扫描，界面回到新的真实状态；勾选也随之回到默认（不记忆）
        await RescanAsync().ConfigureAwait(true);

        if (reviewIndex is not null)
        {
            // 保留"执行集合与已审阅清单不一致"的提示：它是本次清理最需要用户知道的一句话
            StatusMessage = $"清单 {planId} 已执行并完成复核。如果复核报告里还有异常项，请逐条确认后再结束本次清理。"
                            + (reviewBasisChanged
                                ? " 注意：本次执行的集合与你先前导出审阅的清单不一致（勾选被改动过），复核报告以本次执行的清单为准。"
                                : string.Empty);
            ProgressText = StatusMessage;
            ReviewHint = "复核报告已生成，点「查看复核报告」打开。";
            OnPropertyChanged(nameof(ReviewHint));
        }
    }

    /// <summary>
    /// 确认文案必须写清"会发生什么"（需求 3.3-4 / 5.4-5），而不是问"是否继续"。
    /// 同卷/跨卷的落地效果直接引用内核 <see cref="VolumeTextFormatter.DescribeProcessed"/>。
    /// </summary>
    public string BuildConfirmationText(string actionName, IReadOnlyList<CleanItemViewModel> items, bool authorizeHibernate)
    {
        var bytes = items.Sum(i => i.TotalBytes);
        var lines = new List<string>
        {
            $"即将执行：{actionName}",
            string.Empty,
            $"将处理 {items.Count} 项、共 {FormatBytes(bytes)} 个文件。"
        };

        var risky = items.Where(i => i.IsDangerous).ToList();
        if (risky.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("以下项带有需要注意的后果：");
            foreach (var item in risky)
            {
                lines.Add($"· {item.DisplayNameWithNote}：{MessageText.Truncate(item.SideEffect, 160)}");
            }
        }

        lines.Add(string.Empty);
        lines.Add(items.All(i => i.IsRestorable)
            ? $"落地口径：{VolumeTextFormatter.DescribeProcessed(bytes, IsSameVolume)}；隔离区保留期内的文件可以从隔离区还原。"
            : $"落地口径：本批包含不可还原的动作，其中：{VolumeTextFormatter.DescribeProcessed(bytes, IsSameVolume)}。");

        var nonRestorable = items.Where(i => !i.IsRestorable).ToList();
        if (nonRestorable.Count > 0)
        {
            lines.Add("不可还原的项：" + string.Join("、", nonRestorable.Select(i => i.DisplayNameWithNote)));
            foreach (var item in nonRestorable.Where(i => !string.IsNullOrWhiteSpace(i.RestoreHint)))
            {
                lines.Add($"· {item.DisplayName} → {item.RestoreHint}");
            }
        }

        if (authorizeHibernate)
        {
            lines.Add(string.Empty);
            lines.Add("本次勾选了「休眠文件」，需要你的额外授权：");
            lines.Add("· 将执行 powercfg /h off：关闭休眠功能，并连带关闭快速启动（开机与关机速度会变慢一点）；");
            lines.Add("· 不影响睡眠（S3/现代待机），也不影响正常关机与重启；");
            lines.Add("· 该操作可逆：随时可用 powercfg /h on 恢复休眠与快速启动；");
            lines.Add("· 不授权就不会执行这一项（会如实报告为“未授权关闭休眠，已跳过”）。");
        }

        lines.Add(string.Empty);
        lines.Add("确认后才会开始；点取消则不会有任何文件被移动或删除。");
        return MessageText.BuildSummary(lines);
    }

    private void ShowExecutionResult(ExecutionReport report, CleanPlan plan)
    {
        // 同卷/跨卷的文案来自内核，界面只负责摆放（D-6）
        ResultSummaryText = VolumeTextFormatter.DescribeSummary(
            plan.PlannedBytes,
            report.MovedBytes,
            report.SameVolume);

        var lines = new List<string>
        {
            $"计划：{plan.PlanId}（{plan.Items.Count} 项 / {plan.PlannedFileCount} 个文件）",
            $"实际处理：{report.MovedFileCount} 个文件，{FormatBytes(report.MovedBytes)}",
            $"落地口径：{VolumeTextFormatter.DescribeProcessed(report.MovedBytes, report.SameVolume)}"
        };

        if (!string.IsNullOrWhiteSpace(report.BatchId))
        {
            lines.Add($"隔离批次：{report.BatchId}（保留 {_settings.RetentionDays} 天，期间可从隔离区还原）");
        }

        var restorable = report.Items.Where(i => i.ActionKind == CleanActionKind.Quarantine).ToList();
        lines.Add($"可还原项：{restorable.Count} 项（保留期内）");

        if (report.Skipped.Count > 0)
        {
            lines.Add($"跳过 {report.Skipped.Count} 个文件（原因已写入日志与复核报告）：");
            foreach (var skipped in report.Skipped.Take(10))
            {
                lines.Add($"· {skipped.Path} → {skipped.Reason}");
            }

            if (report.Skipped.Count > 10)
            {
                lines.Add($"· 其余 {report.Skipped.Count - 10} 个见复核报告。");
            }
        }
        else
        {
            lines.Add("没有跳过项。");
        }

        var noted = report.Items.Where(i => !string.IsNullOrWhiteSpace(i.Note)).ToList();
        foreach (var item in noted)
        {
            lines.Add($"· {item.DisplayName}：{item.Note}");
        }

        ResultDetailText = MessageText.BuildSummary(lines);
        HasExecutionResult = true;

        StatusMessage = $"执行完成：{report.MovedFileCount} 个文件，{VolumeTextFormatter.DescribeProcessed(report.MovedBytes, report.SameVolume)}。" +
                         (report.Skipped.Count > 0 ? $" 跳过 {report.Skipped.Count} 个文件。" : string.Empty);
        ProgressText = StatusMessage;
        _notifications.Notify($"执行完成：{VolumeTextFormatter.DescribeProcessed(report.MovedBytes, report.SameVolume)}");
    }

    // ── 复核 ──

    /// <summary>按最近一次导出的清单重新做一次复核（需求 3.9-4）。</summary>
    public async Task RunReviewAsync()
    {
        if (_manifestIndex is null)
        {
            StatusMessage = "还没有导出过清单，没有可复核的基准。请先「导出清单」并人工审阅，再执行清理。";
            ProgressText = StatusMessage;
            return;
        }

        IsBusy = true;
        BusyText = "正在复核…";
        try
        {
            var index = _bridge.ReadManifest(_manifestIndex.Directory);
            var movedBytes = _lastPlan is null ? 0 : _lastPlan.PlannedBytes;
            var review = await Task
                .Run(() => _bridge.Review(index, movedBytes, IsSameVolume))
                .ConfigureAwait(true);

            ReviewReportPath = review.MarkdownPath;
            StatusMessage = $"复核完成：已清理 {review.CleanedCount}，未清理 {review.SkippedCount}，异常 {review.UnknownCount}。" +
                            (review.UnknownCount > 0 ? " 异常项必须逐条确认后才能结束本次清理。" : string.Empty);
            ProgressText = StatusMessage;
            ReviewHint = "复核报告已生成，点「查看复核报告」打开。";
            OnPropertyChanged(nameof(ReviewHint));
        }
        catch (Exception ex)
        {
            StatusMessage = $"复核失败：{ex.Message}";
            _notifications.Notify(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>供界面"查看复核报告"按钮调用。</summary>
    public void OpenReviewReport()
    {
        if (HasReviewReport)
        {
            _shell.OpenDirectory(Path.GetDirectoryName(ReviewReportPath) ?? ReportRoot);
        }
        else
        {
            _shell.OpenDirectory(ReportRoot);
        }
    }

    // ── 工具 ──

    /// <summary>当前勾选状态对应的计划（每次都用最新勾选重新构建，清单即执行输入）。</summary>
    private CleanPlan BuildCurrentPlan()
    {
        var report = _lastReport ?? throw new InvalidOperationException("还没有扫描结果，无法构建清单。");
        var plan = _bridge.BuildPlan(report, CheckedIdsFromItems());
        _lastPlan = plan;
        return plan;
    }

    /// <summary>
    /// 按给定项范围构建计划：范围外的项一律不进清单。
    /// "一键清理（L1）"就是靠这个把范围收窄成 L1 的，执行器因此不可能碰到别的分级。
    /// </summary>
    private CleanPlan BuildPlanFor(IReadOnlyList<CleanItemViewModel> scope)
    {
        var report = _lastReport ?? throw new InvalidOperationException("还没有扫描结果，无法构建清单。");
        var plan = _bridge.BuildPlan(report, scope.Select(i => i.ItemId).ToHashSet(StringComparer.Ordinal));
        _lastPlan = plan;
        OnPropertyChanged(nameof(IsSameVolume));
        OnPropertyChanged(nameof(SameVolumeNotice));
        return plan;
    }

    /// <summary>按内核口径描述落地效果（同卷绝不出现"已释放"，需求 3.2-6）。</summary>
    private string DescribeEffect(long bytes) => VolumeTextFormatter.DescribeProcessed(bytes, IsSameVolume);

    /// <summary>
    /// 不带体积的落地效果短句。**逐字对应**内核 <see cref="VolumeTextFormatter.DescribeProcessed"/> 去掉体积后的部分，
    /// 保证"同卷就绝不说已释放"这条口径在摘要句里也不会走样（有测试断言）。
    /// </summary>
    private string DescribeSample() => IsSameVolume
        ? "已移入隔离区（保留期结束或清空隔离区后释放）"
        : "已释放";

    /// <summary>体积格式化统一走内核 <see cref="VolumeTextFormatter"/>，界面不自己算单位。</summary>
    public static string FormatBytes(long bytes) => VolumeTextFormatter.FormatBytes(bytes);

    private string SystemDriveDisplay
    {
        get
        {
            var drive = _lastReport?.Volume.Drive;
            return string.IsNullOrWhiteSpace(drive) ? "系统盘" : drive;
        }
    }

    public static string CategoryText(CleanCategory category) => category switch
    {
        CleanCategory.L1OneClick => "L1 一键直清",
        CleanCategory.L2Recommended => "L2 推荐清理",
        CleanCategory.L3Cautious => "L3 谨慎清理",
        CleanCategory.RecycleBin => "回收站",
        _ => category.ToString()
    };

    public static string CategoryHint(CleanCategory category) => category switch
    {
        CleanCategory.L1OneClick => "系统与软件自己生成的纯缓存，删掉会自动重建；随「一键清理（L1）」执行，不需要逐项确认。",
        CleanCategory.L2Recommended => "可再下载的资源或用户数据，默认只勾选条目自己声明的安全项；请自行确认。",
        CleanCategory.L3Cautious => "可能影响系统功能或不可逆：一律默认不勾，执行前必须二次确认，并就地给出恢复方式。",
        CleanCategory.RecycleBin => "只处理系统盘回收站，按 SID 成对处理元数据与实体文件；默认不勾。",
        _ => string.Empty
    };
}

/// <summary>
/// 一个分级分组（对应一个 hc:Expander）。展开状态由用户控制，重扫时按分类名还原。
/// </summary>
public sealed class CleanSectionViewModel : ViewModelBase
{
    private bool _isExpanded;

    public CleanSectionViewModel(CleanCategory category, IReadOnlyList<CleanItemViewModel> items, bool defaultExpanded)
    {
        Category = category;
        Items = items;
        _isExpanded = defaultExpanded;
    }

    public CleanCategory Category { get; }

    public string Title => MainViewModel.CategoryText(Category);

    /// <summary>
    /// 分级徽标文字（L1 / L2 / L3 / 回收站）。做成两三个字的短标签，是因为它要放进状态色标里，
    /// 而色标是"扫一眼"的东西——"L1 一键直清"塞进去会把色标撑成一条。
    /// </summary>
    public string Badge => Category switch
    {
        CleanCategory.L1OneClick => "L1",
        CleanCategory.L2Recommended => "L2",
        CleanCategory.L3Cautious => "L3",
        _ => "回收站"
    };

    /// <summary>
    /// 分级色调（"safe" / "accent" / "caution" / "info"）：四个分级在列表里必须有**一致的视觉语言**（需求 5.3-4），
    /// 只给语义、不写色值（需求 5.3-1 的令牌集中原则）。
    /// 判据是"删掉之后会发生什么"：L1 会自动重建=安全绿，L2 要重下/重登=主色蓝，
    /// L3 有不可逆风险=注意橙，回收站里的东西是用户自己删的=信息青。
    /// </summary>
    public string Tone => Category switch
    {
        CleanCategory.L1OneClick => "safe",
        CleanCategory.L2Recommended => "accent",
        CleanCategory.L3Cautious => "caution",
        _ => "info"
    };

    public string Hint => MainViewModel.CategoryHint(Category);

    public IReadOnlyList<CleanItemViewModel> Items { get; }

    public int Count => Items.Count;

    public bool HasItems => Items.Count > 0;

    /// <summary>本档**会执行**的体积之和。信息项（页面文件）不算进来，否则档位小计会虚高。</summary>
    public long TotalBytes => Items.Where(i => !i.IsInformationalOnly).Sum(i => i.TotalBytes);

    /// <summary>本档仅展示、不执行的体积（页面文件）。</summary>
    public long InformationalBytes => Items.Where(i => i.IsInformationalOnly).Sum(i => i.TotalBytes);

    public string TotalText => MainViewModel.FormatBytes(TotalBytes);

    /// <summary>
    /// 只有项数的文案（"6 项"）。为什么不直接用 <see cref="CountText"/>：那个里面已经带了体积，
    /// 而体积在界面上就写在这行上面，两处重复会显得啰嗦（并把"项数"这个更有用的信息淹没）。
    /// </summary>
    public string ItemCountText => $"{Count} 项";

    public string CountText => InformationalBytes > 0
        ? $"{Count} 项 / {TotalText}（另有仅展示 {MainViewModel.FormatBytes(InformationalBytes)}，不会清理）"
        : $"{Count} 项 / {TotalText}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
