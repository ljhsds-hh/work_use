using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// 假"已安装程序索引"：可指定"被引用的目录"与"判定即抛异常的目录"。
/// 为什么需要它：注册表内容随机器变化、不可断言，把判定收在接口后面才能穷尽单测
/// （尤其是"判定不确定时必须当作被引用"这条保守性质）。
/// </summary>
internal sealed class FakeInstalledProgramIndex : IInstalledProgramIndex
{
    /// <summary>这棵集合里的目录 = "被某个已安装程序引用"。</summary>
    public HashSet<string> Referenced { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>查询这些目录时抛异常（模拟注册表读取失败）。</summary>
    public HashSet<string> ThrowFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>被查询过的目录（断言"确实问过索引"）。</summary>
    public List<string> Queried { get; } = new();

    public bool IsReferenced(string directoryPath)
    {
        Queried.Add(directoryPath);

        if (ThrowFor.Contains(directoryPath))
        {
            throw new InvalidOperationException("注册表读取失败（假实现，用于验证保守行为）");
        }

        return Referenced.Contains(directoryPath);
    }
}

/// <summary>把日志文本收起来的假日志汇（用于断言"确实记了原因"）。</summary>
internal sealed class RecordingLogSink : ILogSink
{
    public List<string> InfoMessages { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public void Info(string message) => InfoMessages.Add(message);

    public void Warn(string message) => Warnings.Add(message);

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
}

/// <summary>按脚本改写候选集的收窄器：专门用于验证"收窄器只能收窄"这条硬守卫。</summary>
internal sealed class ScriptedSelector : IItemCandidateSelector
{
    private readonly Func<IReadOnlyList<ScanFile>, IReadOnlyList<ScanFile>> _rewrite;

    public ScriptedSelector(string itemId, Func<IReadOnlyList<ScanFile>, IReadOnlyList<ScanFile>> rewrite)
    {
        ItemId = itemId;
        _rewrite = rewrite;
    }

    public string ItemId { get; }

    public int Calls { get; private set; }

    public bool CanHandle(string itemId) => string.Equals(itemId, ItemId, StringComparison.Ordinal);

    public IReadOnlyList<ScanFile> Select(
        CleanItemDefinition item,
        IReadOnlyList<ScanFile> candidates,
        IFileSystem fileSystem,
        ILogSink log)
    {
        Calls++;
        return _rewrite(candidates);
    }
}

/// <summary>
/// 假环境探针：把 <c>%FAKEDATA%</c> 换成给定目录，其余原样返回。
/// 用途：条目里的 <c>%VAR%</c> 模板必须能被展开成真实根目录，<see cref="OrphanDirectorySelector"/> 才认得候选文件。
/// </summary>
internal sealed class FakeEnvironmentProbe : IEnvironmentProbe
{
    public FakeEnvironmentProbe(string programDataRoot) => ProgramDataRoot = programDataRoot;

    public string ProgramDataRoot { get; }

    public bool IsElevated => false;

    public string SystemDrive => "C:";

    public string ExpandVariables(string raw) =>
        raw.Replace("%FAKEDATA%", ProgramDataRoot, StringComparison.OrdinalIgnoreCase);
}

/// <summary>构造测试用的清理项与扫描文件。</summary>
internal static class SelectorTestData
{
    public static CleanItemDefinition Item(string id, params TargetRule[] rules) => new()
    {
        Id = id,
        Category = CleanCategory.L3Cautious,
        DisplayName = "测试项",
        Risk = ItemRisk.Caution,
        ActionKind = CleanActionKind.Quarantine,
        Targets = rules,
        DefaultChecked = false
    };

    public static ScanFile File(string path, long size, DateTimeOffset lastWrite) =>
        new(path, size, lastWrite, CleanActionKind.Quarantine);

    /// <summary>按磁盘上的真实文件构造 ScanFile（体积与最后修改时间都取自文件本身）。</summary>
    public static ScanFile FromDisk(string path)
    {
        var info = new FileInfo(path);
        return new ScanFile(path, info.Length, info.LastWriteTime, CleanActionKind.Quarantine);
    }
}
