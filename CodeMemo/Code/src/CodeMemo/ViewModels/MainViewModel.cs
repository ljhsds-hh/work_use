using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CodeMemo.Helpers;
using CodeMemo.Models;
using CodeMemo.Services;

namespace CodeMemo.ViewModels;

/// <summary>
/// 主窗口视图模型：分类树 + 搜索过滤 + 列表 + 详情 + 增删改 + 导入导出 +
/// 占位符填充 + 常用 Top10 快捷面板 + 外观与常驻偏好。
/// 剪贴板 / 对话框 / 系统外壳 / 外观 / 全局热键全部走注入的接口，因此本类可以脱离 UI 单测。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly LibraryStore _store;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly AppPlatform _platform;
    private readonly DeferredSaver _deferred;
    private CommandLibraryData _data = new();

    /// <summary>树展开态记忆：按（大分类, 子分组）记住用户折叠/展开的选择，跨刷新保留。</summary>
    private readonly Dictionary<(string? Category, string? Group), bool> _expansion = [];

    public MainViewModel(LibraryStore store, SettingsStore settingsStore, AppSettings settings, AppPlatform platform)
    {
        _store = store;
        _settingsStore = settingsStore;
        _settings = settings;
        _platform = platform;
        _deferred = new DeferredSaver(() => _store.Save(_data), platform.Scheduler);

        _selectedSortOption = SortOptions[0];
        _selectedTheme = AppSettingValues.OptionOf(AppSettingValues.ThemeOptions, AppSettingValues.NormalizeTheme(_settings.Theme));
        _selectedFontSize = AppSettingValues.OptionOf(AppSettingValues.FontSizeOptions, AppSettingValues.NormalizeFontSize(_settings.FontSize));
        _globalHotkeyEnabled = _settings.GlobalHotkeyEnabled;
        _minimizeToTray = _settings.MinimizeToTray;

        CopyCommand = new RelayCommand<CommandRowViewModel?>(row => CopyEntryText(row, row?.Entry.Command ?? ""));
        CopyFilledCommand = new RelayCommand(() => CopyEntryText(_selectedCommand, FilledCommandText));
        CopyQuickPickCommand = new RelayCommand<CommandRowViewModel?>(row => CopyEntryText(row, row?.Entry.Command ?? ""));
        AddCommand = new RelayCommand(() => RequestAdd?.Invoke());
        EditCommand = new RelayCommand<CommandRowViewModel?>(OnEdit);
        DeleteCommand = new RelayCommand<CommandRowViewModel?>(OnDelete);
        ImportCommand = new RelayCommand(OnImport);
        ExportCommand = new RelayCommand(OnExport);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        ToggleQuickPickModeCommand = new RelayCommand(() => QuickPickShowRecent = !QuickPickShowRecent);
        ToggleHotkeyRecordingCommand = new RelayCommand(ToggleHotkeyRecording);
        SetHotkeyCommand = new RelayCommand<string?>(gesture => TrySetHotkey(gesture));
        ApplyHotkeyInputCommand = new RelayCommand(ApplyHotkeyInput);
        OpenDataDirectoryCommand = new RelayCommand(() => _platform.Shell.OpenDirectory(_store.DirectoryPath));

        foreach (var option in AppSettingValues.HotkeyOptions)
        {
            HotkeyOptions.Add(option);
        }
        RefreshHotkeyOptions();
    }

    /// <summary>需要给用户轻提示的文案（复制、导入导出、热键注册失败等）。</summary>
    public event Action<string>? Notified;

    /// <summary>请求窗口打开“新增命令”编辑框。</summary>
    public event Action? RequestAdd;

    /// <summary>请求窗口打开“编辑命令”编辑框。</summary>
    public event Action<CommandEntry>? RequestEdit;

    /// <summary>启动时数据文件存在问题（如损坏、版本过高），窗口弹出提示。</summary>
    public event Action<string>? LoadProblemReported;

    /// <summary>全局热键被按下：窗口把自己显示出来并聚焦搜索框。</summary>
    public event Action? HotkeyPressed;

    // ---------- 左侧分类树 ----------

    /// <summary>树的根节点（「全部命令」），其下是大分类与子分组。</summary>
    public ObservableCollection<FilterNode> FilterNodes { get; } = [];

    private FilterNode? _selectedNode;

    /// <summary>当前左侧树选中节点（决定无搜索词时的过滤范围）。</summary>
    public FilterNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
            {
                return;
            }
            // TreeView 双击已选节点会再次触发 SelectedItemChanged，直接刷新即可
            Refresh();
        }
    }

    /// <summary>当前选中节点对应的过滤范围（大分类 / 子分组，null 表示不限定）。</summary>
    public (string? Category, string? Group) CurrentFilterCategory => (_selectedNode?.Category, _selectedNode?.Group);

    // ---------- 搜索与排序 ----------

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }
            Refresh();
            OnPropertyChanged(nameof(IsSearching));
        }
    }
    private string _searchText = "";

    /// <summary>是否处于搜索态（显示“清除”按钮，列表里高亮关键词）。</summary>
    public bool IsSearching => !string.IsNullOrEmpty(_searchText);

    /// <summary>排序选项（含中文标签，供下拉展示）。</summary>
    public sealed record SortOption(SortMode Mode, string Label);

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new(SortMode.Default, "默认顺序"),
        new(SortMode.RecentlyUsed, "最近使用"),
        new(SortMode.MostUsed, "使用最多"),
    ];

    public SortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                _sortMode = value.Mode;
                Refresh();
            }
        }
    }
    private SortOption _selectedSortOption = default!;

    public SortMode SortMode => _sortMode;
    private SortMode _sortMode = SortMode.Default;

    // ---------- 列表与详情 ----------

    public ObservableCollection<CommandRowViewModel> VisibleCommands { get; } = [];

    /// <summary>过滤后条目数描述，列表标题旁展示。</summary>
    public string VisibleCountText
    {
        get => _visibleCountText;
        private set => SetProperty(ref _visibleCountText, value);
    }
    private string _visibleCountText = "";

    /// <summary>当前过滤结果为空（列表区显示空态提示与新增入口）。</summary>
    public bool HasNoResults => VisibleCommands.Count == 0;

    /// <summary>空态文案：搜索态与分类空分组给的引导不同。</summary>
    public string EmptyStateText => _searchText.Length > 0
        ? "没有匹配的命令，换个关键词试试"
        : "这里还没有命令";

    private CommandRowViewModel? _selectedCommand;

    public CommandRowViewModel? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (!SetProperty(ref _selectedCommand, value))
            {
                return;
            }
            OnPropertyChanged(nameof(HasSelected));
            OnPropertyChanged(nameof(DetailCommandText));
            OnPropertyChanged(nameof(DetailNoteText));
            RebuildPlaceholders();
        }
    }

    /// <summary>右侧详情是否可见。</summary>
    public bool HasSelected => _selectedCommand is not null;

    public string DetailCommandText => _selectedCommand?.Entry.Command ?? "";

    public string DetailNoteText => string.IsNullOrWhiteSpace(_selectedCommand?.Entry.Note)
        ? "（暂无备注，点“编辑”补充用途说明）"
        : _selectedCommand.Entry.Note;

    // ---------- 常用 Top10 快捷面板 ----------

    /// <summary>快捷复制面板的条目（只收真正用过的命令）。</summary>
    public ObservableCollection<CommandRowViewModel> QuickPicks { get; } = [];

    /// <summary>是否展示快捷面板（一次都没复制过就没什么可展示）。</summary>
    public bool HasQuickPicks => QuickPicks.Count > 0;

    /// <summary>快捷面板排序：false = 常用（次数优先），true = 最近使用。</summary>
    public bool QuickPickShowRecent
    {
        get => _quickPickShowRecent;
        set
        {
            if (SetProperty(ref _quickPickShowRecent, value))
            {
                RefreshQuickPicks();
            }
        }
    }
    private bool _quickPickShowRecent;

    /// <summary>快捷面板当前排序的中文名（绑定在切换按钮上）。</summary>
    public string QuickPickModeLabel => _quickPickShowRecent ? "最近使用" : "常用";

    // ---------- 外观与常驻偏好 ----------

    public IReadOnlyList<SettingOption> ThemeOptions { get; } = AppSettingValues.ThemeOptions;

    public IReadOnlyList<SettingOption> FontSizeOptions { get; } = AppSettingValues.FontSizeOptions;

    public SettingOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value))
            {
                return;
            }
            _settings.Theme = value.Value;
            SaveSettings();
            _platform.Appearance.Apply(_settings.Theme, _settings.FontSize);
        }
    }
    private SettingOption _selectedTheme;

    public SettingOption SelectedFontSize
    {
        get => _selectedFontSize;
        set
        {
            if (!SetProperty(ref _selectedFontSize, value))
            {
                return;
            }
            _settings.FontSize = value.Value;
            SaveSettings();
            _platform.Appearance.Apply(_settings.Theme, _settings.FontSize);
        }
    }
    private SettingOption _selectedFontSize;

    /// <summary>关闭窗口时收进托盘继续常驻。</summary>
    public bool MinimizeToTrayEnabled
    {
        get => _minimizeToTray;
        set
        {
            if (!SetProperty(ref _minimizeToTray, value))
            {
                return;
            }
            _settings.MinimizeToTray = value;
            SaveSettings();
        }
    }
    private bool _minimizeToTray;

    /// <summary>启用全局热键（改变时立即注册 / 注销）。</summary>
    public bool GlobalHotkeyEnabled
    {
        get => _globalHotkeyEnabled;
        set
        {
            if (!SetProperty(ref _globalHotkeyEnabled, value))
            {
                return;
            }
            _settings.GlobalHotkeyEnabled = value;
            SaveSettings();
            ApplyHotkeySetting();
        }
    }
    private bool _globalHotkeyEnabled;

    /// <summary>当前全局热键手势文本（展示用）。</summary>
    public string HotkeyText => _settings.GlobalHotkey;

    /// <summary>热键候选：几个常用组合 + 当前生效的那个（可能是自己录的）。点一下就设置。</summary>
    public ObservableCollection<SettingOption> HotkeyOptions { get; } = [];

    /// <summary>是否正在等用户按组合键（界面「录制」态）。</summary>
    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        private set
        {
            if (SetProperty(ref _isRecordingHotkey, value))
            {
                OnPropertyChanged(nameof(HotkeyRecordLabel));
            }
        }
    }
    private bool _isRecordingHotkey;

    /// <summary>录制按钮上的文字。</summary>
    public string HotkeyRecordLabel => _isRecordingHotkey ? "取消" : "录制";

    /// <summary>点「录制」开始等按键，再点一次取消。</summary>
    public void ToggleHotkeyRecording()
    {
        IsRecordingHotkey = !IsRecordingHotkey;
        if (_isRecordingHotkey)
        {
            Notified?.Invoke($"请按下要用的组合键（Esc 取消），当前是 {_settings.GlobalHotkey}");
        }
    }

    /// <summary>结束录制态（按下有效组合键或取消时调用）。</summary>
    public void CancelHotkeyRecording() => IsRecordingHotkey = false;

    /// <summary>自定义热键输入框内容（手输或粘贴写法，如 Ctrl+Alt+K）。</summary>
    public string HotkeyInputText
    {
        get => _hotkeyInputText;
        set => SetProperty(ref _hotkeyInputText, value);
    }
    private string _hotkeyInputText = "";

    /// <summary>应用输入框里的写法：成功就回填规范化文本，失败保留内容让用户改。</summary>
    public void ApplyHotkeyInput()
    {
        if (!TrySetHotkey(_hotkeyInputText))
        {
            return;
        }

        HotkeyInputText = _settings.GlobalHotkey;
        Notified?.Invoke($"全局热键已改为 {_settings.GlobalHotkey}");
    }

    /// <summary>
    /// 设置全局热键：写法不合法直接拒绝并提示；新热键被占用时回退到原来那个继续生效。
    /// 返回是否设置成功。
    /// </summary>
    public bool TrySetHotkey(string? gesture)
    {
        if (!HotkeyGesture.TryParse(gesture, out var parsed) || parsed is null)
        {
            Notified?.Invoke("热键不合法：需要「修饰键 + 主键」，例如 Ctrl+Alt+C、Alt+Space、Shift+F9");
            return false;
        }

        var previous = _settings.GlobalHotkey;
        if (parsed.Text == previous)
        {
            RefreshHotkeyOptions();
            return true;
        }

        _settings.GlobalHotkey = parsed.Text;
        SaveSettings();
        OnPropertyChanged(nameof(HotkeyText));
        RefreshHotkeyOptions();

        if (!_settings.GlobalHotkeyEnabled)
        {
            return true;
        }

        if (_platform.Hotkey.TryRegister(parsed.Text, () => HotkeyPressed?.Invoke()))
        {
            return true;
        }

        // 新热键被占用：退回原来那个还在生效的热键，别让用户两头落空
        _settings.GlobalHotkey = previous;
        SaveSettings();
        OnPropertyChanged(nameof(HotkeyText));
        RefreshHotkeyOptions();

        if (!_platform.Hotkey.TryRegister(previous, () => HotkeyPressed?.Invoke()))
        {
            _settings.GlobalHotkeyEnabled = false;
            SaveSettings();
            SetProperty(ref _globalHotkeyEnabled, false, nameof(GlobalHotkeyEnabled));
        }

        Notified?.Invoke($"全局热键 {parsed.Text} 注册失败（可能被其他程序占用），已保留 {previous}");

        // 失败正好发生在处理下拉选择的过程中，直接回写会和它打架；下一个消息循环再同步一次候选显示
        Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(HotkeyText)));
        return false;
    }

    /// <summary>把候选列表与当前热键对齐（自己录的组合会补进候选里）。</summary>
    private void RefreshHotkeyOptions()
    {
        var current = _settings.GlobalHotkey;
        if (HotkeyOptions.All(o => o.Value != current))
        {
            HotkeyOptions.Add(new SettingOption(current, current));
        }
    }

    // ---------- 占位符填充 ----------

    /// <summary>选中命令里的待填参数（&lt;xxx&gt;），详情页据此生成输入框。</summary>
    public ObservableCollection<PlaceholderInput> Placeholders { get; } = [];

    /// <summary>是否显示参数填充区（命令里确实有占位符时）。</summary>
    public bool HasPlaceholders => Placeholders.Count > 0;

    /// <summary>已替换用户填写参数的命令全文（留空的占位符原样保留）。</summary>
    public string FilledCommandText
    {
        get => _filledCommandText;
        private set => SetProperty(ref _filledCommandText, value);
    }
    private string _filledCommandText = "";

    // ---------- 命令 ----------

    public RelayCommand<CommandRowViewModel?> CopyCommand { get; }

    /// <summary>复制「填好参数」的命令（无占位符时等同于复制原文）。</summary>
    public RelayCommand CopyFilledCommand { get; }

    /// <summary>快捷面板里点一下即复制。</summary>
    public RelayCommand<CommandRowViewModel?> CopyQuickPickCommand { get; }

    public RelayCommand AddCommand { get; }

    public RelayCommand<CommandRowViewModel?> EditCommand { get; }

    public RelayCommand<CommandRowViewModel?> DeleteCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    /// <summary>切换快捷面板排序：常用 ⇄ 最近使用。</summary>
    public RelayCommand ToggleQuickPickModeCommand { get; }

    /// <summary>开始 / 取消全局热键录制。</summary>
    public RelayCommand ToggleHotkeyRecordingCommand { get; }

    /// <summary>点候选胶囊设全局热键（参数是手势文本）。</summary>
    public RelayCommand<string?> SetHotkeyCommand { get; }

    /// <summary>应用自定义输入框里的热键写法。</summary>
    public RelayCommand ApplyHotkeyInputCommand { get; }

    /// <summary>在资源管理器里打开数据目录（数据文件、备份与 settings.json 都在那里）。</summary>
    public RelayCommand OpenDataDirectoryCommand { get; }

    // ---------- 初始化 ----------

    /// <summary>启动加载：数据文件不存在则导入内置命令库，存在则读用户数据。</summary>
    public void Initialize()
    {
        if (!_store.Exists)
        {
            _data = SeedLibrary.Load();
            _store.Save(_data);
        }
        else
        {
            var (data, problems) = _store.Load();
            _data = data;
            foreach (var problem in problems)
            {
                LoadProblemReported?.Invoke(problem);
            }
        }
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();
        ApplyHotkeySetting();
    }

    // ---------- 数据操作（供窗口的编辑框回调） ----------

    /// <summary>新增条目。</summary>
    public void AddEntry(CommandEntry entry)
    {
        _data.Commands.Add(entry);
        SaveLibraryNow();
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();
        SelectById(entry.Id);
    }

    /// <summary>编辑保存后刷新展示。</summary>
    public void UpdateEntry(CommandEntry entry)
    {
        entry.UpdatedAt = DateTime.Now;
        SaveLibraryNow();
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();
        SelectById(entry.Id);
    }

    /// <summary>删除条目。</summary>
    public void DeleteEntry(CommandEntry entry)
    {
        _data.Commands.Remove(entry);
        SaveLibraryNow();
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();
        SelectedCommand = null;
    }

    // ---------- 内部实现 ----------

    private void RebuildFilterNodes()
    {
        CaptureExpansionState();

        var root = CommandTreeBuilder.BuildRoot(_data.Commands);
        foreach (var node in Descendants(root))
        {
            if (_expansion.TryGetValue((node.Category, node.Group), out var expanded))
            {
                node.IsExpanded = expanded;
            }
        }

        FilterNodes.Clear();
        FilterNodes.Add(root);

        // 选中节点可能因删除而消失，重建后找不到同范围节点时回落到根
        var old = _selectedNode;
        _selectedNode = old is null ? null : FindNode(FilterNodes, old.Category, old.Group) ?? root;
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(CurrentFilterCategory));
    }

    /// <summary>重建前把当前树上的展开状态记下来（UI 双向绑定已把用户操作写回节点）。</summary>
    private void CaptureExpansionState()
    {
        foreach (var node in FilterNodes.SelectMany(Descendants))
        {
            _expansion[(node.Category, node.Group)] = node.IsExpanded;
        }
    }

    private static IEnumerable<FilterNode> Descendants(FilterNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Descendants))
        {
            yield return child;
        }
    }

    private static FilterNode? FindNode(IEnumerable<FilterNode> nodes, string? category, string? group)
    {
        foreach (var node in nodes)
        {
            if (node.Category == category && node.Group == group)
            {
                return node;
            }
            var hit = FindNode(node.Children, category, group);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    private void Refresh()
    {
        // Clear 会让 ListBox 把选中置空，先记下 Id 才能在重建后把选中与详情还原
        var keepId = _selectedCommand?.Entry.Id;

        var category = _searchText.Length > 0 ? null : _selectedNode?.Category;
        var group = _searchText.Length > 0 ? null : _selectedNode?.Group;
        var matched = CommandSearch.Filter(_data.Commands, _searchText, category, group, _sortMode);

        VisibleCommands.Clear();
        foreach (var entry in matched)
        {
            VisibleCommands.Add(new CommandRowViewModel(entry));
        }

        SelectedCommand = keepId is null ? null : VisibleCommands.FirstOrDefault(r => r.Entry.Id == keepId);
        VisibleCountText = $"共 {matched.Count} 条";
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private void RefreshQuickPicks()
    {
        var entries = _quickPickShowRecent
            ? CommandRanking.TopRecent(_data.Commands)
            : CommandRanking.TopUsed(_data.Commands);

        QuickPicks.Clear();
        foreach (var entry in entries)
        {
            QuickPicks.Add(new CommandRowViewModel(entry));
        }

        OnPropertyChanged(nameof(HasQuickPicks));
        OnPropertyChanged(nameof(QuickPickModeLabel));
    }

    private void SelectById(string id)
    {
        var row = VisibleCommands.FirstOrDefault(r => r.Entry.Id == id);
        if (row is not null)
        {
            SelectedCommand = row;
        }
    }

    private void RebuildPlaceholders()
    {
        Placeholders.Clear();
        foreach (var name in CommandPlaceholders.Extract(_selectedCommand?.Entry.Command))
        {
            Placeholders.Add(new PlaceholderInput(name, UpdateFilledCommandText));
        }
        UpdateFilledCommandText();
        OnPropertyChanged(nameof(HasPlaceholders));
    }

    private void UpdateFilledCommandText()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var placeholder in Placeholders)
        {
            values[placeholder.Name] = placeholder.Value;
        }
        FilledCommandText = CommandPlaceholders.Fill(_selectedCommand?.Entry.Command, values);
    }

    /// <summary>注册 / 注销全局热键；被占用时关掉偏好并让窗口提示。</summary>
    private void ApplyHotkeySetting()
    {
        if (!_settings.GlobalHotkeyEnabled)
        {
            _platform.Hotkey.Unregister();
            return;
        }

        if (_platform.Hotkey.TryRegister(_settings.GlobalHotkey, () => HotkeyPressed?.Invoke()))
        {
            return;
        }

        _settings.GlobalHotkeyEnabled = false;
        SaveSettings();
        SetProperty(ref _globalHotkeyEnabled, false, nameof(GlobalHotkeyEnabled));
        CancelHotkeyRecording();
        Notified?.Invoke($"全局热键 {_settings.GlobalHotkey} 注册失败（可能被其他程序占用），已关闭全局热键");
    }

    private void SaveSettings() => _settingsStore.Save(_settings);

    /// <summary>立刻把命令库写盘：先取消待写的节流任务，再整库写一次（用户显式操作 / 导入 / 退出用）。</summary>
    private void SaveLibraryNow()
    {
        _deferred.CancelPending();
        _store.Save(_data);
    }

    /// <summary>把待写的使用统计落盘（窗口关闭时调用，别让统计丢了）。</summary>
    public void FlushPendingSaves() => _deferred.SaveNow();

    /// <summary>复制指定文本并记账（使用次数 +1、最近复制时间）；文本为空则不动作。</summary>
    private void CopyEntryText(CommandRowViewModel? row, string text)
    {
        if (row is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_platform.Clipboard.TrySetText(text, out var error))
        {
            Notified?.Invoke($"复制失败：{error}");
            return;
        }

        var entry = row.Entry;
        entry.UseCount++;
        entry.LastUsedAt = DateTime.Now;

        // 复制频率高，统计先记着、静默一会儿再整库写盘（退出 / 导入前会强制落盘）
        _deferred.RequestSave();

        // 行内使用次数与详情联动刷新；复制也会改变 Top10 排序
        row.NotifyUsageChanged();
        OnPropertyChanged(nameof(SelectedCommand));
        RefreshQuickPicks();
        Notified?.Invoke($"已复制：{entry.Title}");
    }

    private void OnEdit(CommandRowViewModel? row)
        => RequestEdit?.Invoke(row?.Entry ?? _selectedCommand?.Entry
            ?? throw new InvalidOperationException("没有可编辑的命令"));

    private void OnDelete(CommandRowViewModel? row)
    {
        var entry = row?.Entry ?? _selectedCommand?.Entry;
        if (entry is null)
        {
            return;
        }
        if (_platform.Dialogs.Confirm($"确定删除「{entry.Title}」吗？\n删除后不可恢复。", "删除确认"))
        {
            DeleteEntry(entry);
        }
    }

    private void OnImport()
    {
        var path = _platform.Dialogs.PickOpenFile("导入命令库（JSON）", "JSON 文件 (*.json)|*.json|全部文件 (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        // 先问清是合并还是替换：合并是安全操作，默认就走合并
        var mode = _platform.Dialogs.ChooseImportMode(Path.GetFileName(path));
        if (mode is null)
        {
            return;
        }

        try
        {
            using (var fs = File.OpenRead(path))
            {
                var imported = JsonSerializer.Deserialize<CommandLibraryData>(fs, LibraryStore.JsonOptions)
                    ?? throw new InvalidDataException("文件内容为空");

                var (valid, problems) = LibraryValidator.Validate(imported.Commands);
                if (valid.Count == 0 && imported.Commands.Count > 0)
                {
                    _platform.Dialogs.ShowWarning(
                        $"导入失败，已保持原数据不变：\n文件里的条目都不合规：\n\n{string.Join("\n", problems.Take(5))}",
                        "导入失败");
                    return;
                }

                // 备份的是磁盘上的文件，所以先把待写的使用统计落盘，别让备份落后于内存
                SaveLibraryNow();
                var backup = _store.BackupBeforeImport();

                if (mode == ImportMode.Merge)
                {
                    ApplyMergedImport(valid, imported.Commands.Count, backup);
                }
                else
                {
                    ApplyReplacedImport(valid, imported.Commands.Count, backup);
                }
            }
        }
        catch (Exception ex)
        {
            _platform.Dialogs.ShowWarning($"导入失败，已保持原数据不变：\n{ex.Message}", "导入失败");
        }
    }

    /// <summary>合并导入：新条目追加，重复跳过，同 Id 只更新描述字段。</summary>
    private void ApplyMergedImport(List<CommandEntry> valid, int incomingCount, string? backup)
    {
        var merge = LibraryMerger.Merge(_data.Commands, valid);
        _data = new CommandLibraryData { Commands = merge.Commands };
        _store.Save(_data);
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();

        Notified?.Invoke(
            $"已合并：新增 {merge.Added} 条，更新 {merge.Updated} 条，跳过重复 {merge.Skipped} 条"
            + FormatSkippedProblems(incomingCount - valid.Count)
            + FormatBackupHint(backup));
    }

    /// <summary>整体替换导入（老行为）。</summary>
    private void ApplyReplacedImport(List<CommandEntry> valid, int incomingCount, string? backup)
    {
        _data = new CommandLibraryData { Commands = valid };
        _store.Save(_data);
        RebuildFilterNodes();
        Refresh();
        RefreshQuickPicks();

        Notified?.Invoke(
            $"导入完成，共 {valid.Count} 条"
            + FormatSkippedProblems(incomingCount - valid.Count)
            + FormatBackupHint(backup));
    }

    private static string FormatSkippedProblems(int skipped) => skipped > 0 ? $"，跳过 {skipped} 条不合规条目" : "";

    private static string FormatBackupHint(string? backup)
        => backup is null ? "（原先没有数据文件，未生成备份）" : $"（原数据已备份为 {Path.GetFileName(backup)}）";

    private void OnExport()
    {
        var path = _platform.Dialogs.PickSaveFile("导出命令库（JSON）", "JSON 文件 (*.json)|*.json",
            $"codememo-{DateTime.Now:yyyyMMdd-HHmm}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            using var fs = File.Create(path);
            JsonSerializer.Serialize(fs, _data, LibraryStore.JsonOptions);
            Notified?.Invoke($"已导出 {path}（{_data.Commands.Count} 条）");
        }
        catch (Exception ex)
        {
            _platform.Dialogs.ShowWarning($"导出失败：\n{ex.Message}", "导出失败");
        }
    }
}
