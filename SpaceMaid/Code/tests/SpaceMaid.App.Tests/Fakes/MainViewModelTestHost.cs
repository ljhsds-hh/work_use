using SpaceMaid.App.Services;
using SpaceMaid.App.ViewModels;
using SpaceMaid.Core;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Settings;

namespace SpaceMaid.App.Tests.Fakes;

/// <summary>
/// ViewModel 单测的共用装配：真 <see cref="CoreServices"/>（注入内存假件，不碰真实磁盘）
/// + 脚本化假对话框 / 假文件夹选择器 + 假扫描引擎 / 假执行器。
/// </summary>
public sealed class MainViewModelTestHost
{
    public MainViewModelTestHost(
        long quarantineFreeBytes = 0,
        bool scanRootMissing = false,
        int retentionDays = 7,
        bool elevated = true,
        bool includePageFile = false)
    {
        Settings = new AppSettings
        {
            QuarantineBasePath = @"D:\SpaceMaidQuarantine",
            RetentionDays = retentionDays
        };

        var fileSystem = new InMemoryFileSystem(@"D:\SpaceMaidQuarantine\SpaceMaid\Quarantine", @"C:\");
        Core = CoreServices.Create(
            Settings,
            fileSystem: fileSystem,
            clock: new FixedClock(),
            volumes: new FixedVolumeProbe(),
            environment: new FixedEnvironmentProbe(elevated: elevated),
            log: SilentLogSink.Instance);

        var entries = new List<ScanEntry>
        {
            Entry("l1.user-temp", 1024L * 1024 * 512, 12),
            Entry("l2.browser-cache", 1024L * 1024 * 1024 * 3, 40),
            Entry("l2.windows-old", 1024L * 1024 * 1024 * 8, 900),
            Entry("l3.chat-cache", 1024L * 1024 * 1024 * 5, 60),
            Entry("l3.hibernate", 1024L * 1024 * 1024 * 6, 1),
            Entry("rb.recycle-bin", 1024L * 1024 * 2048, 30)
        };

        // 页面文件（信息项）：默认关掉，只有专门验证"体积口径"的用例打开它
        if (includePageFile)
        {
            entries.Add(Entry("l3.pagefile", 16L * 1024 * 1024 * 1024, 1));
        }

        Report = new ScanReport(
            entries,
            new VolumeSnapshot(@"C:\", 500L * 1024 * 1024 * 1024, 120L * 1024 * 1024 * 1024),
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(8)));

        Scanner = new FakeScanEngine(_ => Report);
        Executor = new FakeCleanExecutor();
        Dialogs = new FakeDialogService();
        FolderPicker = new FakeFolderPicker();
        Notifications = new FakeNotificationService();
        Shell = new FakeShellService();

        Bridge = new CoreServicesBridge(Core);
        ViewModel = new MainViewModel(Bridge, Scanner, Executor, Dialogs, FolderPicker, Notifications, Shell);
    }

    /// <summary>App 侧的内核桥接（生产类型，测试里注入的是内存假件）。</summary>
    public ICoreBridge Bridge { get; }

    public AppSettings Settings { get; }

    public CoreServices Core { get; }

    public ScanReport Report { get; }

    public FakeScanEngine Scanner { get; }

    public FakeCleanExecutor Executor { get; }

    public FakeDialogService Dialogs { get; }

    public FakeFolderPicker FolderPicker { get; }

    public FakeNotificationService Notifications { get; }

    public FakeShellService Shell { get; }

    public MainViewModel ViewModel { get; }

    /// <summary>扫描报告里的条目（体积沿报告给的值，便于断言文案）。</summary>
    public ScanEntry EntryOf(string itemId) => Report.Find(itemId)!;

    private ScanEntry Entry(string itemId, long totalBytes, int fileCount)
    {
        var definition = CleanItemCatalog.ById(itemId)
            ?? throw new InvalidOperationException($"目录里没有 {itemId}");

        var files = Enumerable.Range(1, fileCount)
            .Select(i => new ScanFile(
                $@"C:\test\{itemId}\file{i}.tmp",
                Math.Max(1, totalBytes / fileCount),
                new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.FromHours(8)),
                definition.ActionKind))
            .ToList();

        return new ScanEntry(definition, totalBytes, fileCount, 0, files, true, null);
    }
}

/// <summary>固定时钟：让计划 Id / 时间戳可预测，不依赖真机时间。</summary>
public sealed class FixedClock(DateTimeOffset? now = null) : SpaceMaid.Core.Abstractions.IClock
{
    private DateTimeOffset _now = now ?? new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(8));

    public DateTimeOffset Now => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
