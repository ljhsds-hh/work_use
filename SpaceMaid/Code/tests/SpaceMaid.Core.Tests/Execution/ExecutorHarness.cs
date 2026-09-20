using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Execution;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Safety;
using SpaceMaid.Core.Tests.Abstractions;
using SpaceMaid.Core.Tests.Quarantine;

namespace SpaceMaid.Core.Tests.Execution;

internal sealed class ExecutionFakeClock : IClock
{
    public ExecutionFakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}

/// <summary>执行器测试夹具：真实文件系统 + 临时目录 + 可注入的假卷探针/时钟。</summary>
internal sealed class ExecutorHarness : IDisposable
{
    private static readonly DateTimeOffset Base = new(2026, 9, 20, 14, 30, 0, TimeSpan.FromHours(8));

    public TempRoot Root { get; } = new();

    public WindowsFileSystem FileSystem { get; } = new();

    public MappedVolumeProbe Volumes { get; } = new();

    public ExecutionFakeClock Clock { get; } = new(Base);

    public WindowsEnvironmentProbe Environment { get; } = new();

    public SafetyGate Gate { get; }

    public QuarantineStore Store { get; }

    public QuarantineService Service { get; }

    public CleanExecutor Executor { get; }

    public string QuarantinePath { get; }

    public ExecutorHarness(IEnumerable<ISpecialItemHandler>? handlers = null, string? quarantinePath = null)
    {
        QuarantinePath = quarantinePath ?? Root.Combine("quarantine");
        Gate = new SafetyGate(FileSystem, Environment);
        Store = new QuarantineStore(FileSystem, Volumes, Clock);
        Service = new QuarantineService(Store, FileSystem, Volumes, Clock);
        Executor = new CleanExecutor(Gate, Service, new QuarantinePathValidator(), FileSystem, Volumes, Environment, Clock, handlers);
    }

    /// <summary>构造一致的 (定义 / 扫描条目 / 计划条目) 三元组。</summary>
    public static (CleanItemDefinition Definition, ScanEntry Entry, CleanPlanItem PlanItem) MakeItem(
        string id,
        TargetRule rule,
        IReadOnlyList<ScanFile> files,
        bool userChecked = true,
        CleanActionKind action = CleanActionKind.Quarantine,
        CleanCategory category = CleanCategory.L1OneClick)
    {
        var definition = new CleanItemDefinition
        {
            Id = id,
            Category = category,
            DisplayName = $"项 {id}",
            Risk = ItemRisk.Safe,
            ActionKind = action,
            Targets = new[] { rule },
            AutoRegenerated = category == CleanCategory.L1OneClick,
            DefaultChecked = category == CleanCategory.L1OneClick
        };

        var entry = new ScanEntry(definition, files.Sum(f => f.Size), files.Count, 0, files, true, null);
        var planItem = new CleanPlanItem(
            id,
            category,
            definition.DisplayName,
            "移入隔离区",
            files,
            files.Sum(f => f.Size),
            DefaultChecked: definition.DefaultChecked,
            UserChecked: userChecked)
        {
            ActionKind = action,
            Risk = ItemRisk.Safe
        };

        return (definition, entry, planItem);
    }

    public CleanPlan BuildPlan(IReadOnlyList<CleanPlanItem> items, IReadOnlyList<ScanEntry> entries) =>
        new(
            "20260920-143000-000",
            Base,
            new ScanReport(entries, new VolumeSnapshot(Root.Path, 500L * 1024 * 1024 * 1024, 200L * 1024 * 1024 * 1024), Base),
            items,
            items.Sum(i => i.TotalBytes),
            SameVolumeAsSource: true);

    public ExecutionOptions Options(bool authorizeHibernate = false) => new(
        QuarantinePath,
        7,
        QuarantineOnly: true)
    {
        AuthorizeHibernate = authorizeHibernate
    };

    public ScanFile ScanOf(string path) => new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), CleanActionKind.Quarantine);

    public void Dispose() => Root.Dispose();
}
