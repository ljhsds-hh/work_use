using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using CreateTpl.Helpers;
using CreateTpl.Models;
using CreateTpl.Services;

namespace CreateTpl.ViewModels;

/// <summary>
/// 主界面 ViewModel：编排 输入校验 → 批量创建 → 结果汇总 全流程（需求 3.1 / 3.5 / 3.6）。
/// 不直接操作 UI 控件，通过数据绑定与消息服务与界面交互。
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly IProjectTemplateService _templateService;
    private readonly IFolderPickerService _folderPicker;
    private readonly IMessageService _message;

    private string _rootDirectory = string.Empty;
    private string _projectNamesInput = string.Empty;
    private string _lineNumbersText = "01" + Environment.NewLine;
    private bool _isBusy;
    private bool _isDarkTheme;
    private bool _hasPipelineRun;
    private bool _hasSummary;
    private bool _hasIncomplete;
    private string _summaryText = string.Empty;
    private string _incompleteText = string.Empty;

    public MainViewModel(
        IProjectTemplateService templateService,
        IFolderPickerService folderPicker,
        IMessageService message)
    {
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _message = message ?? throw new ArgumentNullException(nameof(message));

        IsDarkTheme = ThemeService.IsDark;

        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy);
        BrowseCommand = new RelayCommand(_ => Browse(), _ => !IsBusy);
        ClearCommand = new RelayCommand(_ => Clear(), _ => !IsBusy);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
    }

    /// <summary>工程根目录。</summary>
    public string RootDirectory
    {
        get => _rootDirectory;
        set => SetProperty(ref _rootDirectory, value);
    }

    /// <summary>工程名称多行输入（一行一个，支持批量），变更时同步刷新编辑器行号。</summary>
    public string ProjectNamesInput
    {
        get => _projectNamesInput;
        set
        {
            if (SetProperty(ref _projectNamesInput, value))
            {
                LineNumbersText = ComputeLineNumbers(value);
            }
        }
    }

    /// <summary>代码编辑器风格行号文本：未输入前仅显示 01，随输入行数递增。</summary>
    public string LineNumbersText
    {
        get => _lineNumbersText;
        private set => SetProperty(ref _lineNumbersText, value);
    }

    /// <summary>是否正在批量创建中（期间禁用全部按钮，防重复提交）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(EngineStatusText));
            }
        }
    }

    /// <summary>当前是否为暗色主题（Blueprint Studio 昼夜切换）。</summary>
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    /// <summary>流水线是否已执行过至少一次（驱动中栏"待命中"空态与结果区切换）。</summary>
    public bool HasPipelineRun
    {
        get => _hasPipelineRun;
        private set => SetProperty(ref _hasPipelineRun, value);
    }

    /// <summary>是否有可展示的汇总结果。</summary>
    public bool HasSummary
    {
        get => _hasSummary;
        private set => SetProperty(ref _hasSummary, value);
    }

    /// <summary>是否存在未成功创建的工程（需醒目提醒）。</summary>
    public bool HasIncomplete
    {
        get => _hasIncomplete;
        private set => SetProperty(ref _hasIncomplete, value);
    }

    /// <summary>汇总统计文案：成功 / 已存在跳过 / 失败 计数。</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>未完成工程清单及原因（醒目提醒：哪些工程没有生成好）。</summary>
    public string IncompleteText
    {
        get => _incompleteText;
        private set => SetProperty(ref _incompleteText, value);
    }

    /// <summary>顶部状态胶囊文案（引擎待命 / 构建进行中）。</summary>
    public string EngineStatusText => IsBusy ? "Blueprint Building" : "Core Engine Ready";

    /// <summary>逐条创建结果明细。</summary>
    public ObservableCollection<ProjectResult> Results { get; } = new();

    /// <summary>骨架拓扑树（按文件夹层级可折叠）。</summary>
    public ObservableCollection<TreeNodeItem> TreeRoots { get; } = new();

    public ICommand CreateCommand { get; }

    public ICommand BrowseCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    /// <summary>切换昼/夜主题（令牌字典整体替换，全局即时生效）。</summary>
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ThemeService.Apply(IsDarkTheme);
    }

    /// <summary>浏览选择工程根目录。</summary>
    private void Browse()
    {
        var folder = _folderPicker.PickFolder(RootDirectory);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            RootDirectory = folder;
        }
    }

    /// <summary>清空输入与结果，便于继续创建下一批工程（需求 3.5）。</summary>
    private void Clear()
    {
        RootDirectory = string.Empty;
        ProjectNamesInput = string.Empty;
        Results.Clear();
        TreeRoots.Clear();
        SummaryText = string.Empty;
        IncompleteText = string.Empty;
        HasSummary = false;
        HasIncomplete = false;
        HasPipelineRun = false;
    }

    /// <summary>生成模板主流程：校验 → 批量创建 → 汇总反馈。</summary>
    private async Task CreateAsync()
    {
        // ── 第一步：参数校验，校验不通过给出中文提示并终止，不开始生成 ──
        var rootError = InputParser.ValidateRootPath(RootDirectory);
        if (rootError != null)
        {
            _message.Warning(rootError);
            return;
        }

        var parse = InputParser.Parse(ProjectNamesInput);
        if (!parse.IsValid)
        {
            _message.Warning("工程名称校验未通过，请修正后重新生成：\n" + string.Join("\n", parse.Errors));
            return;
        }

        var root = Path.GetFullPath(RootDirectory.Trim());

        // ── 第二步：开始批量创建 ──
        IsBusy = true;
        HasPipelineRun = true;
        Results.Clear();
        TreeRoots.Clear();
        SummaryText = string.Empty;
        IncompleteText = string.Empty;
        HasSummary = false;
        HasIncomplete = false;

        try
        {
            // Progress 回调自动封送到 UI 线程，逐条刷新结果明细
            var progress = new Progress<ProjectResult>(r => Results.Add(r));
            var results = await _templateService.CreateBatchAsync(root, parse.Names, progress);

            // ── 第三步：汇总反馈 ──
            BuildSummary(root, results);

            var failedCount = results.Count(r => r.Status == CreateStatus.Failed);
            var skippedCount = results.Count(r => r.Status == CreateStatus.Skipped);

            if (failedCount > 0)
            {
                _message.Error($"生成完成，但有 {failedCount} 个工程创建失败，请查看未完成清单。");
            }
            else if (skippedCount > 0)
            {
                _message.Warning($"生成完成，{skippedCount} 个工程已存在被跳过，请查看未完成清单。");
            }
            else
            {
                _message.Success($"全部 {results.Count} 个工程创建成功。");
            }
        }
        catch (Exception ex)
        {
            // 全局兜底：任何异常均不导致程序闪退（需求 4：稳定性）
            _message.Error("生成过程中发生异常，已中止：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>汇总展示：统计计数、拓扑树节点、未完成工程醒目清单（需求 3.5 / 3.6）。</summary>
    private void BuildSummary(string root, IReadOnlyList<ProjectResult> results)
    {
        var success = results.Where(r => r.Status == CreateStatus.Success).ToList();
        var skipped = results.Where(r => r.Status == CreateStatus.Skipped).ToList();
        var failed = results.Where(r => r.Status == CreateStatus.Failed).ToList();

        SummaryText = $"共 {results.Count} 个工程：成功 {success.Count}，已存在跳过 {skipped.Count}，失败 {failed.Count}。";
        HasSummary = true;

        // 结构化拓扑树：每个成功工程渲染为可折叠的固定模板目录层级（需求 3.2 / 3.5）
        foreach (var item in success)
        {
            var projectNode = new TreeNodeItem(item.ProjectName + "/", isFolder: true, isProjectRoot: true, path: item.Message)
            {
                IsExpanded = true // 工程根节点默认展开
            };
            projectNode.Children.Add(new TreeNodeItem("CLAUDE.md", path: item.Message));
            projectNode.Children.Add(new TreeNodeItem("README.md", path: item.Message));
            projectNode.Children.Add(new TreeNodeItem("Code/", isFolder: true, path: item.Message));

            var docs = new TreeNodeItem("Docs/", isFolder: true, path: item.Message);
            docs.Children.Add(new TreeNodeItem("需求.md", path: item.Message));
            projectNode.Children.Add(docs);

            TreeRoots.Add(projectNode);
        }

        // 未完成清单（失败 + 已存在跳过），醒目提醒用户哪些工程没有生成好（需求 3.6）
        var incomplete = new StringBuilder();
        foreach (var f in failed)
        {
            incomplete.AppendLine($"【失败】{f.ProjectName}：{f.Message}");
        }

        foreach (var s in skipped)
        {
            incomplete.AppendLine($"【已存在】{s.ProjectName}：{s.Message}");
        }

        if (incomplete.Length > 0)
        {
            IncompleteText = incomplete.ToString().TrimEnd();
            HasIncomplete = true;
        }
    }

    /// <summary>生成编辑器行号文本：未输入前仅显示 01，之后与输入行数保持一致。</summary>
    private static string ComputeLineNumbers(string? input)
    {
        var count = string.IsNullOrEmpty(input)
            ? 1
            : Math.Max(1, input.Replace("\r\n", "\n").Split('\n').Length);

        var sb = new StringBuilder();
        for (var i = 1; i <= count; i++)
        {
            sb.AppendLine(i.ToString("00"));
        }

        return sb.ToString();
    }
}
