using System.IO;
using SpaceMaid.Core;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.App.Services;

/// <summary>
/// 内核桥接：把 <see cref="CoreServices"/> 的几个入口收敛成一组可替换的契约。
///
/// 为什么需要它：ViewModel 必须能脱离真机单测（需求 5 章 + 设计文档 §11 的 ViewModel 单测），
/// 而 <see cref="CoreServices"/> 是 sealed 且构造即绑定真实探针的组合根。
/// 这里只暴露界面真正用到的动作，**不新增任何绕过安全闸门的能力**。
/// </summary>
public interface ICoreBridge
{
    /// <summary>当前生效的设置（只读快照）。</summary>
    AppSettingsView Settings { get; }

    /// <summary>隔离区实际存放根：&lt;基目录&gt;\SpaceMaid\Quarantine。</summary>
    string QuarantineRoot { get; }

    /// <summary>清理项闭集（扫描请求的输入）。</summary>
    IReadOnlyList<CleanItemDefinition> Catalog { get; }

    /// <summary>启动准备（日志滚动 + 隔离区自检 + 到期惰性释放 + 路径校验）。</summary>
    StartupPreparation Prepare();

    /// <summary>由扫描报告生成清理计划（勾选集合为空时取条目自己的 DefaultChecked）。</summary>
    CleanPlan BuildPlan(ScanReport scan, IReadOnlySet<string> checkedItemIds);

    /// <summary>构造执行选项；<paramref name="authorizeHibernate"/> 只能来自用户显式授权。</summary>
    ExecutionOptions BuildExecutionOptions(bool authorizeHibernate);

    /// <summary>校验隔离区候选目录（设置页"选定即校验"）。</summary>
    QuarantinePathValidation ValidateQuarantinePath(string candidate, long requiredBytes = 0);

    /// <summary>隔离区现状（条数 / 体积 / 到期批次数）。</summary>
    QuarantineInfo InspectQuarantine();

    /// <summary>立即清空隔离区（调用方必须先完成二次确认）。</summary>
    ReleaseResult ClearQuarantine();

    /// <summary>惰性释放已到期批次。</summary>
    ReleaseResult ReleaseExpired();

    /// <summary>导出清单（需求 3.9-1：只写清单文件，不碰被扫描目录）。</summary>
    ManifestPaths WriteManifest(CleanPlan plan);

    /// <summary>执行前的清单索引（复核比对依据）。</summary>
    ManifestRowIndex BuildManifestIndex(CleanPlan plan, string directory);

    /// <summary>按清单目录重新读回清单行（复核需要跨次运行可用）。</summary>
    ManifestRowIndex ReadManifest(string directory);

    /// <summary>执行后复核并写出复核报告。</summary>
    ReviewReport Review(ManifestRowIndex index, long releasedBytes, bool sameVolume);

    /// <summary>保存设置（归一化 + 原子写）。</summary>
    bool TrySaveSettings(AppSettingsView settings, out string error);

    /// <summary>清单与复核报告的默认输出根目录。</summary>
    string ReportRoot { get; }

    /// <summary>日志目录。</summary>
    string LogDirectory { get; }

    /// <summary>当前进程是否具备管理员权限（需求 3.6）。</summary>
    bool IsElevated { get; }
}

/// <summary>
/// 设置的界面视图模型快照（只含界面关心的字段，与内核 <c>AppSettings</c> 结构解耦）。
/// </summary>
public sealed record AppSettingsView(
    string QuarantineBasePath,
    int RetentionDays,
    bool IncludeOtherDriveRecycleBin,
    string LogDirectory,
    string ReportDirectory)
{
    public static AppSettingsView From(Core.Settings.AppSettings settings) => new(
        settings.QuarantineBasePath,
        settings.RetentionDays,
        settings.IncludeOtherDriveRecycleBin,
        settings.LogDirectory,
        settings.ReportDirectory);

    public Core.Settings.AppSettings ToCore() => new()
    {
        QuarantineBasePath = QuarantineBasePath,
        RetentionDays = RetentionDays,
        IncludeOtherDriveRecycleBin = IncludeOtherDriveRecycleBin,
        LogDirectory = LogDirectory,
        ReportDirectory = ReportDirectory
    };
}

/// <summary>扫描能力（与内核 <c>IScanEngine</c> 同形，便于测试直接复用）。</summary>
public interface IScanService
{
    Task<ScanReport> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>执行能力（界面只认识这一个动作；安全闸门在实现内部，界面绕不过）。</summary>
public interface ICleanExecutor
{
    Task<ExecutionReport> ExecuteAsync(
        CleanPlan plan,
        ExecutionOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 生产实现：直接转发到 <see cref="CoreServices"/>。
/// 一条纪律：界面层不得复制内核的任何判定逻辑（禁止清单、路径校验、体积口径都取自内核）。
/// </summary>
public sealed class CoreServicesBridge(CoreServices services) : ICoreBridge
{
    private readonly CoreServices _services = services;

    public AppSettingsView Settings => AppSettingsView.From(_services.Settings);

    public IReadOnlyList<CleanItemDefinition> Catalog => _services.Catalog;

    public string QuarantineRoot => _services.QuarantineRoot;

    public string ReportRoot => _services.Settings.ReportDirectory;

    public string LogDirectory => _services.Settings.LogDirectory;

    public bool IsElevated => _services.Environment.IsElevated;

    // 界面启动时才是"用户真的要用这个工具"，允许做删除类维护（日志滚动、账本自检、到期批次释放）；
    // 只读 CLI（--dry-run/--report）走内核默认参数，不做任何删除。
    public StartupPreparation Prepare() => _services.Prepare(allowDestructiveMaintenance: true);

    public CleanPlan BuildPlan(ScanReport scan, IReadOnlySet<string> checkedItemIds)
    {
        // 显式给出勾选集合时，计划里只保留**用户真正勾选**的项（需求 3.3 / D-7：清单即执行输入）。
        // 为什么不能只靠内核的 UserChecked：内核为了让用户看清"哪些项存在"会把未勾选项也留在清单里，
        // 而"一键清理（L1）"这类动作只能拿 L1 的子集去执行，所以在这里按 Id 收窄。
        var plan = _services.BuildPlan(scan, ToOrdinalSet(checkedItemIds));
        if (checkedItemIds.Count == 0)
        {
            return plan;
        }

        return plan with { Items = plan.Items.Where(i => checkedItemIds.Contains(i.ItemId)).ToList() };
    }

    private static HashSet<string>? ToOrdinalSet(IReadOnlySet<string> ids) =>
        ids.Count == 0 ? null : new HashSet<string>(ids, StringComparer.Ordinal);

    public ExecutionOptions BuildExecutionOptions(bool authorizeHibernate) =>
        _services.BuildExecutionOptions(authorizeHibernate);

    public QuarantinePathValidation ValidateQuarantinePath(string candidate, long requiredBytes = 0) =>
        _services.PathValidator.Validate(
            candidate,
            requiredBytes,
            _services.Environment.SystemDrive + Path.DirectorySeparatorChar,
            _services.Volumes,
            _services.FileSystem,
            _services.Environment);

    public QuarantineInfo InspectQuarantine() => _services.Quarantine.Inspect(_services.QuarantineRoot);

    public ReleaseResult ClearQuarantine() => _services.Quarantine.ClearAll(_services.QuarantineRoot);

    public ReleaseResult ReleaseExpired() => _services.Quarantine.ReleaseExpired(_services.QuarantineRoot);

    public ManifestPaths WriteManifest(CleanPlan plan) => _services.Manifest.Write(plan, ReportRoot);

    public ManifestRowIndex BuildManifestIndex(CleanPlan plan, string directory) =>
        ManifestWriter.BuildIndex(plan, directory);

    public ManifestRowIndex ReadManifest(string directory) => ManifestReader.Read(directory);

    public ReviewReport Review(ManifestRowIndex index, long releasedBytes, bool sameVolume) =>
        _services.Reviewer.Review(index, _services.Quarantine.ReadMaps(_services.QuarantineRoot), index.Directory, releasedBytes, sameVolume);

    public bool TrySaveSettings(AppSettingsView settings, out string error) =>
        _services.SettingsStore.TrySave(settings.ToCore(), out error);
}

/// <summary>扫描的生产实现（转发到内核扫描引擎；内核已把重活放在线程池里）。</summary>
public sealed class CoreScanService(CoreServices services) : IScanService
{
    public Task<ScanReport> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        services.Scanner.ScanAsync(request, progress, cancellationToken);
}

/// <summary>执行的生产实现（转发到内核执行器；安全闸门与清单驱动都在内核里，界面无法绕过）。</summary>
public sealed class CoreCleanExecutor(CoreServices services) : ICleanExecutor
{
    public Task<ExecutionReport> ExecuteAsync(
        CleanPlan plan,
        ExecutionOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken cancellationToken) =>
        services.Executor.ExecuteAsync(plan, options, progress, cancellationToken);
}
