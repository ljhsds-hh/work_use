using System.IO;
using SpaceMaid.App.Services;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;

namespace SpaceMaid.App.Tests.Fakes;

/// <summary>假扫描引擎：不做任何 IO，直接回放预先备好的报告。</summary>
public sealed class FakeScanEngine : IScanService
{
    private readonly Func<ScanRequest, ScanReport> _factory;

    public FakeScanEngine(Func<ScanRequest, ScanReport> factory) => _factory = factory;

    public int CallCount { get; private set; }

    public ScanRequest? LastRequest { get; private set; }

    public Task<ScanReport> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        var report = _factory(request);
        progress?.Report(new ScanProgress("test", "测试项", report.TotalBytes, (int)report.TotalFiles));
        return Task.FromResult(report);
    }
}

/// <summary>
/// 假执行器：只统计调用次数与最后一次入参，用来断言"取消确认后执行器零调用"。
/// </summary>
public sealed class FakeCleanExecutor : ICleanExecutor
{
    public int CallCount { get; private set; }

    public CleanPlan? LastPlan { get; private set; }

    public ExecutionOptions? LastOptions { get; private set; }

    /// <summary>供测试在 await 前后记录调用时序。</summary>
    public Action<CleanPlan, ExecutionOptions>? OnExecute { get; set; }

    public Task<ExecutionReport> ExecuteAsync(
        CleanPlan plan,
        ExecutionOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastPlan = plan;
        LastOptions = options;
        OnExecute?.Invoke(plan, options);

        var movedBytes = plan.Checked
            .Where(i => i.ActionKind == CleanActionKind.Quarantine)
            .Sum(i => i.TotalBytes);

        var items = plan.Checked
            .Select(i => new ItemExecutionResult(i.ItemId, i.DisplayName, i.ActionKind, i.Files.Count, i.TotalBytes, 0, null))
            .ToList();

        var report = new ExecutionReport(
            plan.PlanId,
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            items,
            movedBytes,
            SameVolume: true,
            Skipped: Array.Empty<SkippedFile>(),
            BatchId: "20260101-000000-000");

        return Task.FromResult(report);
    }
}

/// <summary>假文件夹选择器：返回预设目录，或 null 表示用户取消。</summary>
public sealed class FakeFolderPicker : IFolderPicker
{
    public string? NextResult { get; set; }

    public int CallCount { get; private set; }

    public string? PickFolder(string title, string? initialDirectory) 
    {
        CallCount++;
        return NextResult;
    }
}

/// <summary>假对话框：可脚本化"用户点确定/取消"，并记录每次确认文案。</summary>
public sealed class FakeDialogService : IDialogService
{
    public FakeDialogService(bool nextConfirmResult = true) => NextConfirmResult = nextConfirmResult;

    public bool NextConfirmResult { get; set; }

    public List<(string Message, string Title)> Confirmations { get; } = new();

    public List<string> Warnings { get; } = new();

    public bool Confirm(string message, string title)
    {
        Confirmations.Add((message, title));
        return NextConfirmResult;
    }

    public void Warn(string message, string title) => Warnings.Add($"{title}：{message}");
}

/// <summary>假轻提示：把 Growl 消息收集起来供断言。</summary>
public sealed class FakeNotificationService : INotificationService
{
    public List<string> Messages { get; } = new();

    public void Notify(string message) => Messages.Add(message);
}

/// <summary>假外壳：只记录"打开哪个目录"，绝不真的启动资源管理器。</summary>
public sealed class FakeShellService : IShellService
{
    public List<string> Opened { get; } = new();

    public void OpenDirectory(string path) => Opened.Add(path);
}

/// <summary>
/// 内存文件系统：只回答"存在吗"，所有写操作一律失败（校验用的最小实现）。
/// </summary>
public sealed class InMemoryFileSystem(params string[] existingPaths) : IFileSystem
{
    private readonly HashSet<string> _paths = new(existingPaths, StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _paths.Contains(path);

    public bool DirectoryExists(string path) => _paths.Contains(path);

    public void CreateDirectory(string path) => _paths.Add(path);

    public long GetFileSize(string path) => 0;

    public DateTimeOffset GetLastWriteTime(string path) => DateTimeOffset.MinValue;

    public DateTimeOffset GetCreationTime(string path) => DateTimeOffset.MinValue;

    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse) => Array.Empty<string>();

    public IReadOnlyList<string> EnumerateDirectories(string directory) => Array.Empty<string>();

    public Stream OpenRead(string path) => Stream.Null;

    public bool TryMove(string source, string destination, out string error)
    {
        error = "内存文件系统不执行移动";
        return false;
    }

    public bool TryCopy(string source, string destination, out string error)
    {
        error = "内存文件系统不执行复制";
        return false;
    }

    public bool TryDeleteFile(string path, out string error)
    {
        error = "内存文件系统不执行删除";
        return false;
    }

    public bool IsReparsePoint(string path) => false;

    public bool HasReparsePointAncestor(string path) => false;

    public bool IsFileLocked(string path) => false;

    public string ComputeHash(string path, bool full) => string.Empty;
}

/// <summary>固定卷探针：所有路径都落在同一卷，可选地被标成可移动介质。</summary>
public sealed class FixedVolumeProbe(string volume = @"C:\", bool removable = false) : IVolumeProbe
{
    public string GetVolumeOf(string path) => volume;

    public long GetFreeBytes(string volume) => 500L * 1024 * 1024 * 1024;

    public bool IsRemovable(string volume) => removable;

    public bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
}

/// <summary>固定环境探针：不依赖真机。</summary>
public sealed class FixedEnvironmentProbe(bool elevated = true, string systemDrive = "C:") : IEnvironmentProbe
{
    public bool IsElevated { get; } = elevated;

    public string SystemDrive { get; } = systemDrive;

    public string ExpandVariables(string raw) => raw;
}
