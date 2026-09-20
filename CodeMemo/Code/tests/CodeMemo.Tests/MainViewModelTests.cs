using System.IO;
using System.Text.Json;
using CodeMemo.Models;
using CodeMemo.Services;
using CodeMemo.ViewModels;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>
/// 主视图模型（脱离 UI 单测）：分类树与计数、刷新保留选中、空态、复制记账、占位符填充、导入校验。
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly List<string> _dirs = [];
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();
    private readonly FakeAppearance _appearance = new();
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeSaveScheduler _scheduler = new();
    private readonly AppSettings _settings = new();

    public void Dispose()
    {
        foreach (var dir in _dirs.Where(Directory.Exists))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codememo-vm-{Guid.NewGuid():N}");
        _dirs.Add(dir);
        return dir;
    }

    private AppPlatform Platform() => new(_clipboard, _dialogs, _shell, _appearance, _hotkey, _scheduler);

    private (MainViewModel Vm, LibraryStore Store, SettingsStore SettingsStore) Create(params CommandEntry[] entries)
    {
        var dir = NewDir();

        var store = new LibraryStore(dir);
        store.Save(new CommandLibraryData { Commands = [.. entries] });

        var settingsStore = new SettingsStore(dir);
        settingsStore.Save(_settings);

        var vm = new MainViewModel(store, settingsStore, _settings, Platform());
        vm.Initialize();
        return (vm, store, settingsStore);
    }

    private static CommandEntry Entry(
        string id, string title, string command, string group = "基础操作", string category = "Git", string note = "")
        => new() { Id = id, Title = title, Command = command, Note = note, Category = category, Group = group };

    private static string WriteImport(string dir, params CommandEntry[] entries)
    {
        var path = Path.Combine(dir, $"import-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(
            new CommandLibraryData { Commands = [.. entries] }, LibraryStore.JsonOptions));
        return path;
    }

    private static CommandEntry[] Sample() =>
    [
        Entry("a", "查看端口占用", "Get-NetTCPConnection -LocalPort <端口号>", "进程与服务", "PowerShell", "排查端口被占用"),
        Entry("b", "压缩目录", "Compress-Archive -Path <源目录> -DestinationPath out.zip", "压缩与归档", "PowerShell"),
    ];

    // ---------- 分类树 ----------

    [Fact]
    public void 自定义子分组在树里可见_父计数等于子计数之和()
    {
        var (vm, _, _) = Create(Entry("x", "我的命令", "echo hi", "我的偷懒分组"));

        var root = vm.FilterNodes.Single();
        var git = root.Children.Single(n => n.Category == "Git");
        var custom = git.Children.Single(n => n.Group == "我的偷懒分组");

        Assert.Equal(1, custom.Count);
        Assert.Equal(git.Count, git.Children.Sum(c => c.Count));
        Assert.Equal(root.Count, root.Children.Sum(c => c.Count));
    }

    [Fact]
    public void 树展开态在重建后保留_子分组默认收起()
    {
        var (vm, _, _) = Create(Entry("x", "我的命令", "echo hi", "我的偷懒分组"));

        var git = vm.FilterNodes.Single().Children.Single(n => n.Category == "Git");
        Assert.True(git.IsExpanded);                                   // 大分类默认展开
        var custom = git.Children.Single(n => n.Group == "我的偷懒分组");
        Assert.False(custom.IsExpanded);                               // 子分组默认收起

        git.IsExpanded = false;
        custom.IsExpanded = true;
        vm.AddEntry(Entry("y", "再来一条", "echo 2", "我的偷懒分组"));

        var gitAgain = vm.FilterNodes.Single().Children.Single(n => n.Category == "Git");
        Assert.False(gitAgain.IsExpanded);
        Assert.True(gitAgain.Children.Single(n => n.Group == "我的偷懒分组").IsExpanded);
    }

    // ---------- 选中与刷新 ----------

    [Fact]
    public void 刷新时按_Id_还原选中项()
    {
        var (vm, _, _) = Create(Sample());

        vm.SelectedCommand = vm.VisibleCommands.Single(r => r.Entry.Id == "a");
        vm.SearchText = "端口";

        Assert.Equal("a", vm.SelectedCommand?.Entry.Id);
        Assert.True(vm.HasSelected);

        // 选中项被过滤掉时详情清空
        vm.SearchText = "压缩";
        Assert.Null(vm.SelectedCommand);
        Assert.False(vm.HasSelected);
    }

    [Fact]
    public void 空结果给出空态文案与计数()
    {
        var (vm, _, _) = Create(Sample());

        vm.SearchText = "找不到的东西";
        Assert.True(vm.HasNoResults);
        Assert.Contains("没有匹配", vm.EmptyStateText);
        Assert.Equal("共 0 条", vm.VisibleCountText);

        vm.ClearSearchCommand.Execute(null);
        Assert.False(vm.HasNoResults);
        Assert.Equal("这里还没有命令", vm.EmptyStateText);
        Assert.Equal(2, vm.VisibleCommands.Count);
    }

    [Fact]
    public void 排序切换后按使用次数排序()
    {
        var (vm, _, _) = Create(Sample());

        vm.SelectedCommand = vm.VisibleCommands.Single(r => r.Entry.Id == "a");
        vm.CopyCommand.Execute(vm.SelectedCommand);
        vm.SelectedSortOption = vm.SortOptions.Single(o => o.Label == "使用最多");

        Assert.Equal("a", vm.VisibleCommands[0].Entry.Id);
    }

    // ---------- 复制与记账 ----------

    [Fact]
    public void 复制写入剪贴板并累加使用次数落盘()
    {
        var (vm, store, _) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        var row = vm.VisibleCommands.Single(r => r.Entry.Id == "a");
        vm.CopyCommand.Execute(row);

        Assert.Equal(row.Entry.Command, _clipboard.LastText);
        Assert.Equal(1, row.Entry.UseCount);
        Assert.NotNull(row.Entry.LastUsedAt);
        Assert.Contains("已复制", Assert.Single(messages));

        // 复制走节流写盘：内存里已经统计到，落盘要等静默期或主动 Flush
        Assert.True(_scheduler.HasPending);
        vm.FlushPendingSaves();

        var (reloaded, _) = store.Load();
        Assert.Equal(1, reloaded.Commands.Single(c => c.Id == "a").UseCount);
    }

    // ---------- 节流写盘 ----------

    [Fact]
    public void 复制是节流写盘_静默期到点才落盘()
    {
        var (vm, store, _) = Create(Sample());
        var row = vm.VisibleCommands.Single(r => r.Entry.Id == "a");

        vm.CopyCommand.Execute(row);
        vm.CopyCommand.Execute(row);

        Assert.Equal(2, row.Entry.UseCount);
        Assert.True(_scheduler.HasPending);
        Assert.Equal(0, store.Load().Data.Commands.Single(c => c.Id == "a").UseCount);   // 还没写盘

        _scheduler.Fire();

        Assert.False(_scheduler.HasPending);
        Assert.Equal(2, store.Load().Data.Commands.Single(c => c.Id == "a").UseCount);
    }

    [Fact]
    public void 主动操作立刻整库落盘_待写统计一起写下去()
    {
        var (vm, store, _) = Create(Sample());
        vm.CopyCommand.Execute(vm.VisibleCommands.Single(r => r.Entry.Id == "a"));

        vm.AddEntry(Entry("new", "新命令", "echo new"));

        Assert.False(_scheduler.HasPending);
        var reloaded = store.Load().Data;
        Assert.Equal(1, reloaded.Commands.Single(c => c.Id == "a").UseCount);
        Assert.Contains(reloaded.Commands, c => c.Id == "new");
    }

    [Fact]
    public void 剪贴板写入失败时提示且不记账()
    {
        var (vm, _, _) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;
        _clipboard.Succeed = false;

        var row = vm.VisibleCommands.Single(r => r.Entry.Id == "a");
        vm.CopyCommand.Execute(row);

        Assert.Equal(0, row.Entry.UseCount);
        Assert.Contains("复制失败", Assert.Single(messages));
    }

    [Fact]
    public void 删除需确认_确认后选中回落为空()
    {
        var (vm, _, _) = Create(Sample());
        vm.SelectedCommand = vm.VisibleCommands.Single(r => r.Entry.Id == "a");

        vm.DeleteCommand.Execute(vm.SelectedCommand);
        Assert.Equal(2, vm.VisibleCommands.Count);    // 未确认不删
        Assert.NotEmpty(_dialogs.Confirms);

        _dialogs.ConfirmResult = true;
        vm.DeleteCommand.Execute(vm.SelectedCommand);

        Assert.Single(vm.VisibleCommands);
        Assert.Equal("b", vm.VisibleCommands[0].Entry.Id);
        Assert.Null(vm.SelectedCommand);
    }

    // ---------- 占位符填充 ----------

    [Fact]
    public void 选中命令后生成参数输入项_填好后可复制()
    {
        var (vm, _, _) = Create(Sample());
        vm.SelectedCommand = vm.VisibleCommands.Single(r => r.Entry.Id == "a");

        Assert.True(vm.HasPlaceholders);
        var input = Assert.Single(vm.Placeholders);
        Assert.Equal("端口号", input.Name);
        Assert.Equal("Get-NetTCPConnection -LocalPort <端口号>", vm.FilledCommandText);

        input.Value = "8080";
        Assert.Equal("Get-NetTCPConnection -LocalPort 8080", vm.FilledCommandText);

        vm.CopyFilledCommand.Execute(null);
        Assert.Equal("Get-NetTCPConnection -LocalPort 8080", _clipboard.LastText);
        Assert.Equal(1, vm.SelectedCommand!.Entry.UseCount);
    }

    [Fact]
    public void 无占位符时隐藏参数区且填充结果等于原文()
    {
        var (vm, _, _) = Create(Entry("c", "看状态", "git status -sb"));

        vm.SelectedCommand = vm.VisibleCommands.Single();

        Assert.False(vm.HasPlaceholders);
        Assert.Equal("git status -sb", vm.FilledCommandText);
    }

    // ---------- 导入 / 导出 / 数据目录 ----------

    [Fact]
    public void 导入跳过不合规条目并生成时间戳备份()
    {
        var (vm, store, _) = Create(Entry("old", "旧命令", "echo old"));
        var messages = new List<string>();
        vm.Notified += messages.Add;

        var importPath = Path.Combine(store.DirectoryPath, "import.json");
        File.WriteAllText(importPath, JsonSerializer.Serialize(new CommandLibraryData
        {
            Commands =
            [
                Entry("n1", "新命令", "echo new"),
                new CommandEntry { Id = "bad", Title = "  ", Command = "echo", Category = "Git", Group = "基础操作" },
            ],
        }, LibraryStore.JsonOptions));
        _dialogs.OpenFile = importPath;

        vm.ImportCommand.Execute(null);

        Assert.Equal("n1", Assert.Single(vm.VisibleCommands).Entry.Id);
        var backup = Assert.Single(Directory.GetFiles(store.DirectoryPath, "commands.*.bak"));
        Assert.Contains("旧命令", File.ReadAllText(backup));
        Assert.Contains("跳过 1 条", messages.Last());
    }

    [Fact]
    public void 导入文件全部不合规时保持原数据不变()
    {
        var (vm, store, _) = Create(Entry("old", "旧命令", "echo old"));

        var importPath = Path.Combine(store.DirectoryPath, "import.json");
        File.WriteAllText(importPath, JsonSerializer.Serialize(new CommandLibraryData
        {
            Commands = [new CommandEntry { Id = "bad", Title = "", Command = "", Category = "Git", Group = "基础操作" }],
        }, LibraryStore.JsonOptions));
        _dialogs.OpenFile = importPath;

        vm.ImportCommand.Execute(null);

        Assert.Equal("old", Assert.Single(vm.VisibleCommands).Entry.Id);
        Assert.Contains("导入失败", _dialogs.Warnings.Single().Message);
    }

    [Fact]
    public void 导出写出可读的中文与尖括号()
    {
        var (vm, store, _) = Create(Sample());
        var exportPath = Path.Combine(store.DirectoryPath, "export.json");
        _dialogs.SaveFile = exportPath;

        vm.ExportCommand.Execute(null);

        var text = File.ReadAllText(exportPath);
        Assert.Contains("查看端口占用", text);
        Assert.Contains("<端口号>", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void 打开数据目录交给外壳服务()
    {
        var (vm, store, _) = Create(Sample());

        vm.OpenDataDirectoryCommand.Execute(null);

        Assert.Equal(store.DirectoryPath, _shell.OpenedDirectory);
    }

    [Fact]
    public void 新增条目后自动选中且树里能看到()
    {
        var (vm, _, _) = Create(Sample());

        vm.AddEntry(Entry("new", "新命令", "echo new", "我的新分组"));

        Assert.Equal("new", vm.SelectedCommand?.Entry.Id);
        Assert.Contains(vm.FilterNodes.Single().Children.Single(n => n.Category == "Git").Children,
            n => n.Group == "我的新分组");
    }

    // ---------- 快捷复制 Top10 ----------

    [Fact]
    public void 快捷面板_没用过时不显示_复制后出现()
    {
        var (vm, _, _) = Create(Sample());

        Assert.False(vm.HasQuickPicks);
        Assert.Empty(vm.QuickPicks);

        vm.CopyCommand.Execute(vm.VisibleCommands.Single(r => r.Entry.Id == "a"));

        Assert.True(vm.HasQuickPicks);
        Assert.Equal("a", Assert.Single(vm.QuickPicks).Entry.Id);
    }

    [Fact]
    public void 快捷面板_切换常用与最近使用_点一下即复制()
    {
        var (vm, _, _) = Create(Entry("a", "甲命令", "echo 1"), Entry("b", "乙命令", "echo 2"));
        var entries = vm.VisibleCommands.ToDictionary(r => r.Entry.Id, r => r.Entry);
        entries["a"].UseCount = 5;
        entries["a"].LastUsedAt = new DateTime(2026, 9, 1);
        entries["b"].UseCount = 1;
        entries["b"].LastUsedAt = new DateTime(2026, 9, 10);

        vm.ToggleQuickPickModeCommand.Execute(null);       // 切到「最近使用」
        Assert.Equal("最近使用", vm.QuickPickModeLabel);
        Assert.Equal(["b", "a"], vm.QuickPicks.Select(r => r.Entry.Id).ToArray());

        vm.ToggleQuickPickModeCommand.Execute(null);       // 切回「常用」
        Assert.Equal("常用", vm.QuickPickModeLabel);
        Assert.Equal(["a", "b"], vm.QuickPicks.Select(r => r.Entry.Id).ToArray());

        var row = vm.QuickPicks.Single(r => r.Entry.Id == "b");
        vm.CopyQuickPickCommand.Execute(row);

        Assert.Equal(row.Entry.Command, _clipboard.LastText);
        Assert.Equal(2, row.Entry.UseCount);
    }

    [Fact]
    public void 快捷面板只收用过的条目且不超过十条()
    {
        var entries = Enumerable.Range(0, 15)
            .Select(i => Entry($"c{i:00}", $"命令{i:00}", $"echo {i}"))
            .ToArray();
        var (vm, _, _) = Create(entries);

        foreach (var id in entries.Select(e => e.Id))
        {
            var row = vm.VisibleCommands.Single(r => r.Entry.Id == id);
            var times = id == "c00" ? 3 : 1;
            for (var i = 0; i < times; i++)
            {
                vm.CopyCommand.Execute(row);
            }
        }

        Assert.Equal(CommandRanking.DefaultSize, vm.QuickPicks.Count);
        Assert.Equal("c00", vm.QuickPicks[0].Entry.Id);
    }

    // ---------- 外观与常驻偏好 ----------

    [Fact]
    public void 切换主题与字号_落盘并通知外观服务()
    {
        var (vm, _, settingsStore) = Create(Sample());

        vm.SelectedTheme = vm.ThemeOptions.Single(o => o.Value == AppSettingValues.ThemeDark);
        vm.SelectedFontSize = vm.FontSizeOptions.Single(o => o.Value == AppSettingValues.FontLarge);

        Assert.Equal(2, _appearance.Applied.Count);
        Assert.Equal(AppSettingValues.ThemeDark, _appearance.Applied[0].Theme);
        Assert.Equal(AppSettingValues.FontLarge, _appearance.Applied[1].FontSize);

        var saved = settingsStore.Load();
        Assert.Equal(AppSettingValues.ThemeDark, saved.Theme);
        Assert.Equal(AppSettingValues.FontLarge, saved.FontSize);
    }

    [Fact]
    public void 常驻开关会落盘()
    {
        var (vm, _, settingsStore) = Create(Sample());
        Assert.True(vm.MinimizeToTrayEnabled);

        vm.MinimizeToTrayEnabled = false;

        Assert.False(settingsStore.Load().MinimizeToTray);
    }

    [Fact]
    public void 全局热键按偏好注册_按下时通知窗口()
    {
        var (vm, _, settingsStore) = Create(Sample());
        Assert.Equal(new[] { AppSettingValues.DefaultHotkey }, _hotkey.Registered);
        Assert.Equal(AppSettingValues.DefaultHotkey, vm.HotkeyText);
        Assert.True(vm.GlobalHotkeyEnabled);

        var pressed = false;
        vm.HotkeyPressed += () => pressed = true;
        _hotkey.Press();
        Assert.True(pressed);

        vm.GlobalHotkeyEnabled = false;
        Assert.Equal(1, _hotkey.UnregisterCount);
        Assert.False(settingsStore.Load().GlobalHotkeyEnabled);
    }

    [Fact]
    public void 全局热键被占用时_关闭偏好并通知窗口()
    {
        _hotkey.Succeed = false;

        var dir = NewDir();
        var store = new LibraryStore(dir);
        store.Save(new CommandLibraryData { Commands = [.. Sample()] });
        var settingsStore = new SettingsStore(dir);
        settingsStore.Save(_settings);

        var vm = new MainViewModel(store, settingsStore, _settings, Platform());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        vm.Initialize();

        Assert.False(vm.GlobalHotkeyEnabled);
        Assert.Contains("已关闭全局热键", Assert.Single(messages));
        Assert.False(settingsStore.Load().GlobalHotkeyEnabled);
    }

    // ---------- 全局热键：候选 / 录制 ----------

    [Fact]
    public void 热键可从候选里改_并立即重新注册()
    {
        var (vm, _, settingsStore) = Create(Sample());
        Assert.Equal(AppSettingValues.HotkeyOptions.Count, vm.HotkeyOptions.Count);

        vm.SetHotkeyCommand.Execute("Alt+Space");

        Assert.Equal("Alt+Space", vm.HotkeyText);
        Assert.Equal("Alt+Space", settingsStore.Load().GlobalHotkey);
        Assert.Equal("Alt+Space", _hotkey.Registered.Last());
        Assert.Equal("Alt+Space", vm.HotkeyText);
    }

    [Fact]
    public void 热键录制_非法组合被拒绝且保留原值()
    {
        var (vm, _, settingsStore) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        Assert.False(vm.TrySetHotkey("鼠标中键"));
        Assert.False(vm.TrySetHotkey(null));
        Assert.False(vm.TrySetHotkey("Ctrl"));

        Assert.Equal(AppSettingValues.DefaultHotkey, settingsStore.Load().GlobalHotkey);
        Assert.Equal(3, messages.Count);
        Assert.All(messages, message => Assert.Contains("不合法", message));
    }

    [Fact]
    public void 录制出来的热键写法会被规范化()
    {
        var (vm, _, settingsStore) = Create(Sample());

        Assert.True(vm.TrySetHotkey("ctrl+alt+space"));

        Assert.Equal("Ctrl+Alt+Space", vm.HotkeyText);
        Assert.Equal("Ctrl+Alt+Space", settingsStore.Load().GlobalHotkey);
        Assert.Contains(vm.HotkeyOptions, o => o.Value == "Ctrl+Alt+Space");
    }

    [Fact]
    public void 新热键被占用时_回退到原热键并提示()
    {
        var (vm, _, settingsStore) = Create(Sample());
        _hotkey.Rejected.Add("Ctrl+Alt+Space");
        var messages = new List<string>();
        vm.Notified += messages.Add;

        vm.SetHotkeyCommand.Execute("Ctrl+Alt+Space");

        Assert.Equal(AppSettingValues.DefaultHotkey, vm.HotkeyText);            // 回退到真实生效值
        Assert.Equal(AppSettingValues.DefaultHotkey, settingsStore.Load().GlobalHotkey);
        Assert.True(vm.GlobalHotkeyEnabled);
        Assert.Contains("已保留", Assert.Single(messages));
    }

    [Fact]
    public void 热键禁用状态下改热键_只保存不注册()
    {
        var (vm, _, settingsStore) = Create(Sample());
        vm.GlobalHotkeyEnabled = false;
        var registeredBefore = _hotkey.Registered.Count;

        Assert.True(vm.TrySetHotkey("Ctrl+Shift+C"));

        Assert.Equal("Ctrl+Shift+C", settingsStore.Load().GlobalHotkey);
        Assert.Equal(registeredBefore, _hotkey.Registered.Count);
    }

    [Fact]
    public void 录制态切换与按钮文案()
    {
        var (vm, _, _) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        Assert.False(vm.IsRecordingHotkey);
        Assert.Equal("录制", vm.HotkeyRecordLabel);

        vm.ToggleHotkeyRecordingCommand.Execute(null);
        Assert.True(vm.IsRecordingHotkey);
        Assert.Equal("取消", vm.HotkeyRecordLabel);
        Assert.Contains("请按下要用的组合键", Assert.Single(messages));

        vm.ToggleHotkeyRecordingCommand.Execute(null);
        Assert.False(vm.IsRecordingHotkey);
        Assert.Equal("录制", vm.HotkeyRecordLabel);
    }

    [Fact]
    public void 录制用自己录的组合_会补进候选()
    {
        var (vm, _, _) = Create(Sample());

        Assert.True(vm.TrySetHotkey("Ctrl+Shift+F12"));

        Assert.Contains(vm.HotkeyOptions, o => o.Value == "Ctrl+Shift+F12");
        Assert.Equal("Ctrl+Shift+F12", vm.HotkeyText);
        Assert.Equal(AppSettingValues.HotkeyOptions.Count + 1, vm.HotkeyOptions.Count);
    }

    // ---------- 自定义热键输入框 ----------

    [Fact]
    public void 自定义热键输入_合法则规范化并落盘()
    {
        var (vm, _, settingsStore) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        vm.HotkeyInputText = "  ctrl + alt + K  ";
        vm.ApplyHotkeyInputCommand.Execute(null);

        Assert.Equal("Ctrl+Alt+K", vm.HotkeyInputText);              // 回填规范化写法
        Assert.Equal("Ctrl+Alt+K", vm.HotkeyText);
        Assert.Equal("Ctrl+Alt+K", settingsStore.Load().GlobalHotkey);
        Assert.Equal("Ctrl+Alt+K", _hotkey.Registered.Last());
        Assert.Contains(vm.HotkeyOptions, o => o.Value == "Ctrl+Alt+K");
        Assert.Contains("已改为 Ctrl+Alt+K", Assert.Single(messages));
    }

    [Fact]
    public void 自定义热键输入_非法则保留内容并提示()
    {
        var (vm, _, settingsStore) = Create(Sample());
        var messages = new List<string>();
        vm.Notified += messages.Add;

        vm.HotkeyInputText = "鼠标中键";
        vm.ApplyHotkeyInputCommand.Execute(null);

        Assert.Equal("鼠标中键", vm.HotkeyInputText);                // 内容留着，方便用户改
        Assert.Equal(AppSettingValues.DefaultHotkey, settingsStore.Load().GlobalHotkey);
        Assert.Contains("不合法", Assert.Single(messages));
    }

    // ---------- 导入：合并 / 替换 / 取消 ----------

    [Fact]
    public void 导入合并模式_新增不重复的条目()
    {
        var (vm, store, _) = Create(Entry("a", "看状态", "git status -sb"));
        var messages = new List<string>();
        vm.Notified += messages.Add;

        _dialogs.OpenFile = WriteImport(store.DirectoryPath,
            Entry("a", "看状态", "git status -sb"),          // 内容重复 → 跳过
            Entry("b", "看日志", "git log"));                 // 新增
        _dialogs.ImportModeResult = ImportMode.Merge;

        vm.ImportCommand.Execute(null);

        // 落盘顺序是「现有在前 + 新增追加」；界面列表按默认排序展示，所以这里查库文件
        var merged = store.Load().Data.Commands;
        Assert.Equal(["a", "b"], merged.Select(c => c.Id).ToArray());
        Assert.Equal(2, vm.VisibleCommands.Count);
        Assert.Contains("新增 1 条", messages.Last());
        Assert.Contains("跳过重复 1 条", messages.Last());
    }

    [Fact]
    public void 导入合并模式_同_Id_更新描述但保留本机统计()
    {
        var (vm, store, _) = Create(Entry("a", "旧标题", "git status"));
        var row = vm.VisibleCommands.Single();
        vm.CopyCommand.Execute(row);                          // 制造一条使用统计
        vm.FlushPendingSaves();

        _dialogs.OpenFile = WriteImport(store.DirectoryPath, Entry("a", "新标题", "git status -sb", note: "新备注"));
        _dialogs.ImportModeResult = ImportMode.Merge;

        vm.ImportCommand.Execute(null);

        var merged = Assert.Single(store.Load().Data.Commands);
        Assert.Equal("新标题", merged.Title);
        Assert.Equal("git status -sb", merged.Command);
        Assert.Equal("新备注", merged.Note);
        Assert.Equal(1, merged.UseCount);                     // 本机统计没被导入文件覆盖
    }

    [Fact]
    public void 导入替换模式_覆盖现有库()
    {
        var (vm, store, _) = Create(Entry("a", "看状态", "git status -sb"));

        _dialogs.OpenFile = WriteImport(store.DirectoryPath, Entry("z", "看日志", "git log"));
        _dialogs.ImportModeResult = ImportMode.Replace;

        vm.ImportCommand.Execute(null);

        Assert.Equal("z", Assert.Single(store.Load().Data.Commands).Id);
    }

    [Fact]
    public void 导入时选择取消_什么都不做()
    {
        var (vm, store, _) = Create(Entry("a", "看状态", "git status -sb"));

        _dialogs.OpenFile = WriteImport(store.DirectoryPath, Entry("z", "看日志", "git log"));
        _dialogs.ImportModeResult = null;

        vm.ImportCommand.Execute(null);

        Assert.Equal("a", Assert.Single(store.Load().Data.Commands).Id);
        Assert.Empty(Directory.GetFiles(store.DirectoryPath, "commands.*.bak"));
        Assert.Single(_dialogs.ImportModeAsked);
    }

    [Fact]
    public void 导入前先把待写统计落盘_备份里能看到()
    {
        var (vm, store, _) = Create(Entry("a", "看状态", "git status -sb"));
        vm.CopyCommand.Execute(vm.VisibleCommands.Single());   // 统计还在节流里没落盘

        _dialogs.OpenFile = WriteImport(store.DirectoryPath, Entry("z", "看日志", "git log"));
        _dialogs.ImportModeResult = ImportMode.Replace;

        vm.ImportCommand.Execute(null);

        var backup = Assert.Single(Directory.GetFiles(store.DirectoryPath, "commands.*.bak"));
        Assert.Contains("\"UseCount\": 1", File.ReadAllText(backup));
    }
}
