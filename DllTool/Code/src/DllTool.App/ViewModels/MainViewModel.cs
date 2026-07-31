using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DllTool.App.Services;
using DllTool.Core.Models;
using DllTool.Core.Services;
using DllTool.Core.Validation;
using DllTool.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MessageBox = HandyControl.Controls.MessageBox;

namespace DllTool.App.ViewModels;

/// <summary>
/// 主界面视图模型：承载模式切换、目录选择、流程执行与结果展示。
/// 模式A与模式B各自拥有独立的输入与结果状态，切换模式时互不干扰。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FlowValidator _validator;
    private readonly ModeAExecutor _modeA;
    private readonly ModeBExecutor _modeB;
    private readonly IOverwriteConfirmation _overwriteConfirmation;
    private readonly AppSettings _settings;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private bool _isModeA = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>背景图片路径（null 表示不使用背景）。</summary>
    [ObservableProperty]
    private string? _backgroundImagePath;

    /// <summary>背景显示强度（0-100，0=不显示，100=最强）。持久化。</summary>
    [ObservableProperty]
    private double _backgroundStrength = 40;

    // 模式A 专属状态
    [ObservableProperty]
    private string _newDllDirectory = string.Empty;

    [ObservableProperty]
    private string _backupRootA = string.Empty;

    [ObservableProperty]
    private string? _backupDirectoryDisplayA;

    [ObservableProperty]
    private string? _manifestPathDisplayA;

    // 模式B 专属状态
    [ObservableProperty]
    private string _manifestFile = string.Empty;

    [ObservableProperty]
    private string _backupRootB = string.Empty;

    [ObservableProperty]
    private string? _backupDirectoryDisplayB;

    [ObservableProperty]
    private string? _manifestPathDisplayB;

    // 两模式共用的目标目录
    [ObservableProperty]
    private string _targetDirectory = string.Empty;

    public ObservableCollection<OperationEntryRow> ResultRows { get; } = [];

    // ============ 随模式切换的"当前活动"显示属性 ============

    /// <summary>当前模式生效的备份根目录。</summary>
    public string ActiveBackupRoot => IsModeA ? BackupRootA : BackupRootB;

    /// <summary>当前模式生效的备份目录（结果）。</summary>
    public string? ActiveBackupDirectory => IsModeA ? BackupDirectoryDisplayA : BackupDirectoryDisplayB;

    /// <summary>当前模式生效的备份清单路径（结果）。</summary>
    public string? ActiveManifestPath => IsModeA ? ManifestPathDisplayA : ManifestPathDisplayB;

    partial void OnBackupRootAChanged(string value) => OnPropertyChanged(nameof(ActiveBackupRoot));
    partial void OnBackupRootBChanged(string value) => OnPropertyChanged(nameof(ActiveBackupRoot));
    partial void OnBackupDirectoryDisplayAChanged(string? value) => OnPropertyChanged(nameof(ActiveBackupDirectory));
    partial void OnBackupDirectoryDisplayBChanged(string? value) => OnPropertyChanged(nameof(ActiveBackupDirectory));
    partial void OnManifestPathDisplayAChanged(string? value) => OnPropertyChanged(nameof(ActiveManifestPath));
    partial void OnManifestPathDisplayBChanged(string? value) => OnPropertyChanged(nameof(ActiveManifestPath));

    private ModeABackupResult? _backupResult;
    private OperationResult? _lastOperationResult;

    public MainViewModel(
        FlowValidator validator,
        ModeAExecutor modeA,
        ModeBExecutor modeB,
        IOverwriteConfirmation overwriteConfirmation,
        AppSettings settings,
        ILogger<MainViewModel> logger,
        FileLoggerProvider fileLoggerProvider)
    {
        _validator = validator;
        _modeA = modeA;
        _modeB = modeB;
        _overwriteConfirmation = overwriteConfirmation;
        _settings = settings;
        _logger = logger;

        _settings.Load();
        BackgroundImagePath = _settings.BackgroundImagePath;
        BackgroundStrength = _settings.BackgroundStrength;

        _logger.LogInformation("主界面视图模型已初始化，日志目录：{Directory}", fileLoggerProvider.LogDirectory);
    }

    /// <summary>背景图片本身的不透明度（随强度增加而增强）。</summary>
    public double BackgroundImageOpacity => 0.02 + BackgroundStrength * 0.0085;

    /// <summary>内容遮罩的不透明度（随强度增加而降低，保证可读性）。</summary>
    public double BackgroundOverlayOpacity => Math.Max(0.35, 1.0 - BackgroundStrength * 0.007);

    partial void OnBackgroundImagePathChanged(string? value) => _settings.Save();

    partial void OnBackgroundStrengthChanged(double value)
    {
        OnPropertyChanged(nameof(BackgroundImageOpacity));
        OnPropertyChanged(nameof(BackgroundOverlayOpacity));
        _settings.Save();
    }

    /// <summary>切换模式时，先保存离开模式的当前状态，再加载目标模式的独立状态。</summary>
    partial void OnIsModeAChanged(bool value)
    {
        if (value)
        {
            // 从模式B 切到模式A：先保存模式B，再加载模式A
            SaveModeBState();
            NewDllDirectory = _modeAState.NewDllDirectory;
            TargetDirectory = _modeAState.TargetDirectory;
            BackupRootA = _modeAState.BackupRoot;
            BackupDirectoryDisplayA = _modeAState.BackupDirectory;
            ManifestPathDisplayA = _modeAState.ManifestPath;
            LoadRows(_modeAState.Rows);
            StatusText = _modeAState.StatusText;
        }
        else
        {
            // 从模式A 切到模式B：先保存模式A，再加载模式B
            SaveModeAState();
            ManifestFile = _modeBState.ManifestFile;
            TargetDirectory = _modeBState.TargetDirectory;
            BackupRootB = _modeBState.BackupRoot;
            BackupDirectoryDisplayB = _modeBState.BackupDirectory;
            ManifestPathDisplayB = _modeBState.ManifestPath;
            LoadRows(_modeBState.Rows);
            StatusText = _modeBState.StatusText;
        }

        // 显式刷新随模式切换的"当前活动"显示属性，避免残留另一模式的值。
        OnPropertyChanged(nameof(ActiveBackupRoot));
        OnPropertyChanged(nameof(ActiveBackupDirectory));
        OnPropertyChanged(nameof(ActiveManifestPath));
    }

    // ============ 模式A 输入选择 ============

    public void PickNewDllDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "选择新DLL文件夹" };
        if (dialog.ShowDialog() == true)
        {
            NewDllDirectory = dialog.FolderName;
            _logger.LogInformation("选择新DLL文件夹：{Path}", dialog.FolderName);
        }
    }

    public void PickTargetDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "选择目标目录" };
        if (dialog.ShowDialog() == true)
        {
            TargetDirectory = dialog.FolderName;
            _logger.LogInformation("选择目标目录：{Path}", dialog.FolderName);
        }
    }

    public void PickManifestFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择DLL清单文本文件",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ManifestFile = dialog.FileName;
            _logger.LogInformation("选择DLL清单文件：{Path}", dialog.FileName);
        }
    }

    public void PickBackupRootA()
    {
        var dialog = new OpenFolderDialog { Title = "选择备份存储根目录（模式A）" };
        if (dialog.ShowDialog() == true)
        {
            BackupRootA = dialog.FolderName;
            _logger.LogInformation("选择备份根目录（模式A）：{Path}", dialog.FolderName);
        }
    }

    public void PickBackupRootB()
    {
        var dialog = new OpenFolderDialog { Title = "选择备份存储根目录（模式B）" };
        if (dialog.ShowDialog() == true)
        {
            BackupRootB = dialog.FolderName;
            _logger.LogInformation("选择备份根目录（模式B）：{Path}", dialog.FolderName);
        }
    }

    /// <summary>选择背景图片。</summary>
    public void PickBackgroundImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            BackgroundImagePath = dialog.FileName;
            _logger.LogInformation("设置背景图片：{Path}", dialog.FileName);
        }
    }

    /// <summary>清除背景图片。</summary>
    public void ClearBackgroundImage()
    {
        BackgroundImagePath = null;
        _logger.LogInformation("清除背景图片");
    }

    // ============ 流程执行 ============

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsBusy)
            return;

        try
        {
            if (IsModeA)
            {
                await ExecuteModeAAsync();
            }
            else
            {
                await ExecuteModeBAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流程执行异常：{Message}", ex.Message);
            ShowError($"流程执行异常：{ex.Message}");
        }
    }

    private async Task ExecuteModeAAsync()
    {
        string? error = _validator.ValidateModeA(NewDllDirectory, TargetDirectory);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupRootA))
        {
            ShowError("请选择备份存储根目录。");
            return;
        }

        string? backupError = _validator.ValidateBackupRoot(BackupRootA, TargetDirectory, NewDllDirectory);
        if (backupError is not null)
        {
            ShowError(backupError);
            return;
        }

        // 阶段1：静默备份（后台线程执行）。
        ModeABackupResult backupResult = await RunInBackgroundAsync(() => _modeA.Backup(NewDllDirectory, TargetDirectory, BackupRootA));
        _backupResult = backupResult;

        if (backupResult.MatchedRelativePaths.Count == 0)
        {
            StatusText = "两端无重合的DLL文件，流程结束。";
            ShowError("两端无重合的DLL文件，未执行任何备份与覆盖操作。");
            return;
        }

        UpdateUiFromBackup(backupResult);

        // 阶段2：覆盖确认弹窗（备份完成后弹出）。
        bool allowOverwrite = ConfirmOverwrite();

        if (!allowOverwrite)
        {
            _logger.LogInformation("用户在覆盖确认弹窗中选择【取消】，仅执行备份。");
            return;
        }

        // 覆盖执行（后台线程）。
        OperationResult result = await RunInBackgroundAsync(() => _modeA.Overwrite(NewDllDirectory, TargetDirectory, backupResult));
        _lastOperationResult = result;
        await UpdateUiAsync(result);
    }

    private async Task ExecuteModeBAsync()
    {
        string? error = _validator.ValidateModeB(ManifestFile, TargetDirectory);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupRootB))
        {
            ShowError("请选择备份存储根目录。");
            return;
        }

        string? backupError = _validator.ValidateBackupRoot(BackupRootB, TargetDirectory, ManifestFile);
        if (backupError is not null)
        {
            ShowError(backupError);
            return;
        }

        OperationResult result = await RunInBackgroundAsync(() => _modeB.Execute(ManifestFile, TargetDirectory, BackupRootB));
        _lastOperationResult = result;
        await UpdateUiAsync(result);
    }

    /// <summary>耗时文件操作在后台线程执行，期间禁用交互。</summary>
    private async Task<T> RunInBackgroundAsync<T>(Func<T> action)
    {
        IsBusy = true;
        StatusText = "正在执行，请稍候…";
        try
        {
            return await Task.Run(action);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateUiAsync(OperationResult result)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LoadRows(result.Entries.Select(OperationEntryRow.From));
            StatusText = BuildStatusText(result);

            if (IsModeA)
            {
                BackupDirectoryDisplayA = result.BackupDirectory;
                ManifestPathDisplayA = result.ManifestPath;
                SaveModeAState();
            }
            else
            {
                BackupDirectoryDisplayB = result.BackupDirectory;
                ManifestPathDisplayB = result.ManifestPath;
                SaveModeBState();
            }
        });
    }

    /// <summary>备份完成后先展示备份阶段明细，再弹覆盖确认窗。</summary>
    private void UpdateUiFromBackup(ModeABackupResult backupResult)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LoadRows(backupResult.Entries.Select(OperationEntryRow.From));
            BackupDirectoryDisplayA = backupResult.BackupDirectory;
            ManifestPathDisplayA = backupResult.ManifestPath;
            StatusText = $"备份完成：成功 {backupResult.BackupSucceeded} 个" +
                         (backupResult.BackupFailed > 0 ? $"，失败 {backupResult.BackupFailed} 个" : "") +
                         "。等待覆盖确认。";
            SaveModeAState();
        });
    }

    private bool ConfirmOverwrite()
    {
        return _overwriteConfirmation.Confirm();
    }

    private string BuildStatusText(OperationResult result)
    {
        var parts = new List<string> { $"备份成功 {result.BackupSucceeded} 个" };
        if (result.BackupFailed > 0)
            parts.Add($"备份失败 {result.BackupFailed} 个");
        if (result.OverwriteExecuted)
        {
            parts.Add($"覆盖成功 {result.OverwriteSucceeded} 个");
            if (result.OverwriteFailed > 0)
                parts.Add($"覆盖失败 {result.OverwriteFailed} 个");
        }
        if (result.SkippedCount > 0)
            parts.Add($"跳过 {result.SkippedCount} 个");
        return string.Join("，", parts) + "。";
    }

    // ============ 模式状态保存 / 恢复 ============

    /// <summary>模式A 独立状态。</summary>
    private sealed class ModeState
    {
        public string NewDllDirectory { get; set; } = string.Empty;
        public string ManifestFile { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public string BackupRoot { get; set; } = string.Empty;
        public string? BackupDirectory { get; set; }
        public string? ManifestPath { get; set; }
        public string StatusText { get; set; } = "就绪";
        public List<OperationEntryRow> Rows { get; } = [];
    }

    private readonly ModeState _modeAState = new();
    private readonly ModeState _modeBState = new();

    private void SaveModeAState()
    {
        _modeAState.NewDllDirectory = NewDllDirectory;
        _modeAState.TargetDirectory = TargetDirectory;
        _modeAState.BackupRoot = BackupRootA;
        _modeAState.BackupDirectory = BackupDirectoryDisplayA;
        _modeAState.ManifestPath = ManifestPathDisplayA;
        _modeAState.StatusText = StatusText;
        _modeAState.Rows.Clear();
        _modeAState.Rows.AddRange(ResultRows);
    }

    private void SaveModeBState()
    {
        _modeBState.ManifestFile = ManifestFile;
        _modeBState.TargetDirectory = TargetDirectory;
        _modeBState.BackupRoot = BackupRootB;
        _modeBState.BackupDirectory = BackupDirectoryDisplayB;
        _modeBState.ManifestPath = ManifestPathDisplayB;
        _modeBState.StatusText = StatusText;
        _modeBState.Rows.Clear();
        _modeBState.Rows.AddRange(ResultRows);
    }

    private void LoadRows(IEnumerable<OperationEntryRow> rows)
    {
        ResultRows.Clear();
        foreach (var row in rows)
        {
            ResultRows.Add(row);
        }
    }

    private void ShowError(string message)
    {
        _logger.LogWarning("界面提示：{Message}", message);
        MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>
/// 结果表格行视图模型。
/// </summary>
public sealed class OperationEntryRow
{
    public required string RelativePath { get; init; }
    public required string Action { get; init; }
    public required string ResultText { get; init; }
    public string? Reason { get; init; }

    public bool IsFailed => ResultText == "失败";
    public bool IsBackupOnly => ResultText == "仅备份";
    public bool IsBackup => Action == OperationActions.Backup;
    public bool IsOverwrite => Action == OperationActions.Overwrite;

    /// <summary>行首状态指示条颜色：失败红、跳过灰、仅备份蓝、成功绿。</summary>
    public Brush StatusBarBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0x0E, 0x9F, 0x6E));

    /// <summary>操作类型胶囊背景色：备份蓝、覆盖橙。</summary>
    public Brush ActionChipBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE));

    /// <summary>操作类型文字颜色。</summary>
    public Brush ActionTextBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0x2F, 0x54, 0xEB));

    public static OperationEntryRow From(OperationEntry entry)
    {
        string resultText = entry.Status switch
        {
            OperationStatus.Success => "成功",
            OperationStatus.Failed => "失败",
            OperationStatus.Skipped => "跳过",
            OperationStatus.BackupOnly => "仅备份",
            _ => entry.Status.ToString()
        };

        var row = new OperationEntryRow
        {
            RelativePath = entry.RelativePath,
            Action = entry.Action,
            ResultText = resultText,
            Reason = entry.Reason
        };

        // 状态指示条颜色
        row.StatusBarBrush = resultText switch
        {
            "失败" => new SolidColorBrush(Color.FromRgb(0xE0, 0x3B, 0x45)),
            "跳过" => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            "仅备份" => new SolidColorBrush(Color.FromRgb(0x2F, 0x54, 0xEB)),
            _ => new SolidColorBrush(Color.FromRgb(0x0E, 0x9F, 0x6E))
        };

        // 操作类型胶囊配色
        if (entry.Action == OperationActions.Backup)
        {
            row.ActionChipBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE));
            row.ActionTextBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x54, 0xEB));
        }
        else
        {
            row.ActionChipBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xEF, 0xE6));
            row.ActionTextBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x5A, 0x1A));
        }

        return row;
    }
}
