using System.IO;
using SpaceMaid.App.Helpers;
using SpaceMaid.App.Services;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Quarantine;

namespace SpaceMaid.App.ViewModels;

/// <summary>
/// 设置页 ViewModel：隔离区位置（选定即校验）、保留天数、回收站范围、立即清空隔离区、报告与日志入口。
///
/// 两条纪律：
/// ① **不碰 UI 服务**：文件夹选择、确认框、轻提示、打开目录都通过接口注入（需求/设计 §11 ViewModel 单测）；
/// ② **没有绕过开关**：这里不存在"跳过确认 / 强制清理"之类的属性（需求 4.3-5 / 不变量 I-6，由单测用反射守住）。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ICoreBridge _bridge;
    private readonly IFolderPicker _folderPicker;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    private string _quarantineBasePath;
    private int _retentionDays;
    private bool _includeOtherDriveRecycleBin;
    private QuarantinePathLevel _validationLevel = QuarantinePathLevel.Ok;
    private string _validationMessage = string.Empty;
    private string _validationDetail = string.Empty;
    private bool _isDirty;

    public SettingsViewModel(
        ICoreBridge bridge,
        IFolderPicker folderPicker,
        IDialogService dialogs,
        INotificationService notifications,
        IShellService shell)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));

        var settings = bridge.Settings;
        _quarantineBasePath = settings.QuarantineBasePath;
        _retentionDays = settings.RetentionDays;
        _includeOtherDriveRecycleBin = settings.IncludeOtherDriveRecycleBin;

        PickQuarantineFolderCommand = new RelayCommand(() => PickQuarantineFolder());
        SaveCommand = new RelayCommand(() => Save());
        ClearQuarantineCommand = new RelayCommand(() => ClearQuarantineNow());
        OpenReportDirectoryCommand = new RelayCommand(OpenReportDirectory);
        OpenLogDirectoryCommand = new RelayCommand(OpenLogDirectory);

        Validate();
    }

    public System.Windows.Input.ICommand PickQuarantineFolderCommand { get; }

    public System.Windows.Input.ICommand SaveCommand { get; }

    public System.Windows.Input.ICommand ClearQuarantineCommand { get; }

    public System.Windows.Input.ICommand OpenReportDirectoryCommand { get; }

    public System.Windows.Input.ICommand OpenLogDirectoryCommand { get; }

    /// <summary>创建并完成首次校验（设置窗口打开时就有结论，不让用户先猜）。</summary>
    public static SettingsViewModel Create(
        ICoreBridge bridge,
        IFolderPicker folderPicker,
        IDialogService dialogs,
        INotificationService notifications,
        IShellService shell) =>
        new(bridge, folderPicker, dialogs, notifications, shell);

    /// <summary>打开目录用（报告 / 日志入口）。</summary>
    public IShellService Shell { get; }

    /// <summary>隔离区**基目录**（默认 <c>%LOCALAPPDATA%</c>，需求 3.4-1）。</summary>
    public string QuarantineBasePath
    {
        get => _quarantineBasePath;
        set
        {
            if (SetProperty(ref _quarantineBasePath, value ?? string.Empty))
            {
                _isDirty = true;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                Validate();
            }
        }
    }

    /// <summary>隔离区实际存放位置（需求 3.4-2：只在选定目录下建 SpaceMaid\Quarantine）。</summary>
    public string QuarantineStoragePath =>
        string.IsNullOrWhiteSpace(QuarantineBasePath)
            ? string.Empty
            : Path.Combine(QuarantineBasePath, "SpaceMaid", "Quarantine");

    /// <summary>保留天数（1–365，需求 3.4-5）。越界值夹回范围内而不是静默接受。</summary>
    public int RetentionDays
    {
        get => _retentionDays;
        set
        {
            var clamped = Math.Clamp(value, 1, 365);
            if (SetProperty(ref _retentionDays, clamped))
            {
                _isDirty = true;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(RetentionText));
            }
        }
    }

    public string RetentionText => $"保留 {RetentionDays} 天，到期后在下一次启动时自动释放";

    /// <summary>是否把非系统盘回收站也纳入（默认关，需求 2.5）。</summary>
    public bool IncludeOtherDriveRecycleBin
    {
        get => _includeOtherDriveRecycleBin;
        set
        {
            if (SetProperty(ref _includeOtherDriveRecycleBin, value))
            {
                _isDirty = true;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(RecycleBinScopeText));
            }
        }
    }

    public string RecycleBinScopeText => IncludeOtherDriveRecycleBin
        ? "系统盘与非系统盘的回收站都会进入清单（默认关闭，开启后请自行确认其它盘的回收站内容）"
        : "只处理系统盘的回收站（默认；非系统盘回收站需要显式开启）";

    /// <summary>校验级别：Ok / Warn / Reject。Reject 时禁止保存（需求 3.4-2）。</summary>
    public QuarantinePathLevel ValidationLevel
    {
        get => _validationLevel;
        private set
        {
            if (SetProperty(ref _validationLevel, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(ValidationLevelText));
                OnPropertyChanged(nameof(IsRejected));
                OnPropertyChanged(nameof(IsWarning));
            }
        }
    }

    /// <summary>校验结论（内核给的整句中文，界面不再自行拼文案）。</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>技术细节（卷 / 目录），用于排障展示。</summary>
    public string ValidationDetail
    {
        get => _validationDetail;
        private set => SetProperty(ref _validationDetail, value);
    }

    public string ValidationLevelText => ValidationLevel switch
    {
        QuarantinePathLevel.Ok => "可用",
        QuarantinePathLevel.Warn => "可以保存，但不建议",
        _ => "不可用，不能保存"
    };

    public bool IsRejected => ValidationLevel == QuarantinePathLevel.Reject;

    public bool IsWarning => ValidationLevel == QuarantinePathLevel.Warn;

    /// <summary>Reject 不允许保存（需求 3.4-2 / 7 章第 7 条）。</summary>
    public bool CanSave => ValidationLevel != QuarantinePathLevel.Reject;

    public bool HasUnsavedChanges => _isDirty;

    /// <summary>隔离区当前位置与占用（界面上让用户知道"东西放哪、占了多少"）。</summary>
    public string QuarantineLocationText => _bridge.QuarantineRoot;

    public string ReportDirectory => _bridge.ReportRoot;

    public string LogDirectory => _bridge.LogDirectory;

    /// <summary>保存状态文案（保存失败必须让用户看见，不能静默）。</summary>
    public string SaveStatusMessage { get; private set; } = string.Empty;

    /// <summary>点"选择文件夹"：选完立即校验，结果自动显示（需求 3.4-2）。</summary>
    public bool PickQuarantineFolder()
    {
        var picked = _folderPicker.PickFolder("选择隔离区位置（实际存放于其下的 SpaceMaid\\Quarantine）", QuarantineBasePath);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return false;
        }

        QuarantineBasePath = picked;
        return true;
    }

    /// <summary>保存设置；Reject 时直接拒绝并返回 false。</summary>
    public bool Save()
    {
        if (!CanSave)
        {
            SaveStatusMessage = "当前隔离区位置不可用，未保存。请先换一个位置。";
            OnPropertyChanged(nameof(SaveStatusMessage));
            _notifications.Notify(SaveStatusMessage);
            return false;
        }

        var candidate = _bridge.Settings with
        {
            QuarantineBasePath = QuarantineBasePath,
            RetentionDays = RetentionDays,
            IncludeOtherDriveRecycleBin = IncludeOtherDriveRecycleBin
        };

        if (!_bridge.TrySaveSettings(candidate, out var error))
        {
            SaveStatusMessage = error;
            OnPropertyChanged(nameof(SaveStatusMessage));
            _notifications.Notify(error);
            return false;
        }

        _isDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(QuarantineLocationText));
        SaveStatusMessage = "设置已保存。隔离区位置的变更会在下一次清理时生效。";
        OnPropertyChanged(nameof(SaveStatusMessage));
        _notifications.Notify(SaveStatusMessage);
        return true;
    }

    /// <summary>打开清单与复核报告目录。</summary>
    public void OpenReportDirectory() => Shell.OpenDirectory(ReportDirectory);

    /// <summary>打开日志目录。</summary>
    public void OpenLogDirectory() => Shell.OpenDirectory(LogDirectory);

    /// <summary>
    /// 立即清空隔离区（需求 3.4-5）：必须先二次确认，文案写明**不可还原**；
    /// 用户取消则返回 null 且**不调用任何清空动作**。
    /// </summary>
    public ReleaseResult? ClearQuarantineNow()
    {
        var info = _bridge.InspectQuarantine();
        var message =
            $"将立即清空隔离区，**彻底删除**其中全部 {info.Batches.Count} 个批次。" + Environment.NewLine + Environment.NewLine +
            $"清空后这些文件**不可还原**（不再有保留期保护），也无法从回收站找回。" + Environment.NewLine +
            $"隔离区当前占用约 {Core.Reporting.VolumeTextFormatter.FormatBytes(info.TotalBytes)}。" + Environment.NewLine + Environment.NewLine +
            "确定要立即清空吗？";

        if (!_dialogs.Confirm(message, "立即清空隔离区（不可还原）"))
        {
            _notifications.Notify("已取消，隔离区未做任何改动。");
            return null;
        }

        var result = _bridge.ClearQuarantine();
        _notifications.Notify(
            $"隔离区已清空：{result.ReleasedBatches} 个批次、{Core.Reporting.VolumeTextFormatter.FormatBytes(result.ReleasedBytes)}。");
        OnPropertyChanged(nameof(QuarantineUsageText));
        return result;
    }

    /// <summary>隔离区占用一句话（清空前后都会刷新）。</summary>
    public string QuarantineUsageText
    {
        get
        {
            var info = _bridge.InspectQuarantine();
            return $"{info.Batches.Count} 个批次，共 {Core.Reporting.VolumeTextFormatter.FormatBytes(info.TotalBytes)}"
                   + (info.ExpiredBatchCount > 0 ? $"（其中 {info.ExpiredBatchCount} 个已到期）" : string.Empty);
        }
    }

    /// <summary>
    /// 重新校验（内核 <c>QuarantinePathValidator</c> 是唯一判定来源：网络路径、系统目录、可写性、
    /// 同卷提醒、跨卷空间都在那里，界面不复制这些规则）。
    /// </summary>
    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(QuarantineBasePath))
        {
            ValidationLevel = QuarantinePathLevel.Reject;
            ValidationMessage = "请先指定隔离区位置";
            ValidationDetail = string.Empty;
            return;
        }

        var validation = _bridge.ValidateQuarantinePath(QuarantineBasePath);
        ValidationLevel = validation.Level;
        ValidationMessage = validation.Message;
        ValidationDetail = validation.Detail;
    }
}
