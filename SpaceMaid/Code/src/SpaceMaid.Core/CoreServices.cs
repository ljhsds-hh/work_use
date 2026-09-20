using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Execution;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Quarantine;
using SpaceMaid.Core.Reporting;
using SpaceMaid.Core.Safety;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Settings;

namespace SpaceMaid.Core;

/// <summary>启动准备结果（每次启动都要做的惰性工作）。</summary>
/// <param name="Recovery">断电/强退后的隔离区账本自检结果。</param>
/// <param name="Release">到期批次的惰性释放结果（无常驻进程，只能在这里做）。</param>
/// <param name="QuarantineUsable">隔离区路径当前是否可用。</param>
/// <param name="QuarantineMessage">可直接展示给用户的中文说明。</param>
public sealed record StartupPreparation(
    RecoverReport Recovery,
    ReleaseResult Release,
    bool QuarantineUsable,
    string QuarantineMessage);

/// <summary>
/// 内核组合根：把安全闸门、扫描、隔离区、执行器、报告串成一条链，界面只认识这一个对象。
///
/// 为什么集中在一处：整条链路的不变量（先扫描后执行、全量隔离、清单驱动、禁止清单硬拦截）
/// 只有在同一处装配时才能保证不被绕过；界面层拿不到也不该拿到更底层的零件。
/// </summary>
public sealed class CoreServices
{
    private CoreServices(
        AppSettings settings,
        SettingsStore settingsStore,
        ILogSink log,
        IFileSystem fileSystem,
        IClock clock,
        IVolumeProbe volumes,
        IEnvironmentProbe environment,
        ICommandRunner commandRunner,
        IRecycleBinScanner recycleBin)
    {
        Settings = settings;
        SettingsStore = settingsStore;
        Log = log;
        FileSystem = fileSystem;
        Clock = clock;
        Volumes = volumes;
        Environment = environment;

        SafetyGate = new SafetyGate(fileSystem, environment, log);
        Scanner = new ScanEngine(fileSystem, environment, volumes, clock, capacity: null, recycleBin: recycleBin, log: log);
        QuarantineStore = new QuarantineStore(fileSystem, volumes, clock, log);
        Quarantine = new QuarantineService(QuarantineStore, fileSystem, volumes, clock, log);
        PathValidator = new QuarantinePathValidator();
        SpecialHandlers = new ISpecialItemHandler[]
        {
            new HibernateGate(commandRunner, fileSystem, environment, log),
            new DismComponentCleanup(commandRunner, log)
        };
        Executor = new CleanExecutor(
            SafetyGate,
            Quarantine,
            PathValidator,
            fileSystem,
            volumes,
            environment,
            clock,
            SpecialHandlers,
            log);
        Manifest = new ManifestWriter();
        Reviewer = new ReviewReporter(fileSystem, clock, log);
        Catalog = CleanItemCatalog.All;
    }

    public AppSettings Settings { get; }

    public SettingsStore SettingsStore { get; }

    public ILogSink Log { get; }

    public IFileSystem FileSystem { get; }

    public IClock Clock { get; }

    public IVolumeProbe Volumes { get; }

    public IEnvironmentProbe Environment { get; }

    public SafetyGate SafetyGate { get; }

    public IScanEngine Scanner { get; }

    public QuarantineStore QuarantineStore { get; }

    public QuarantineService Quarantine { get; }

    public QuarantinePathValidator PathValidator { get; }

    public CleanExecutor Executor { get; }

    public ManifestWriter Manifest { get; }

    public ReviewReporter Reviewer { get; }

    public IReadOnlyList<ISpecialItemHandler> SpecialHandlers { get; }

    public IReadOnlyList<CleanItemDefinition> Catalog { get; }

    /// <summary>隔离区实际存放根（用户选择的基目录 + SpaceMaid\Quarantine）。</summary>
    public string QuarantineRoot => Path.Combine(Settings.QuarantineBasePath, "SpaceMaid", "Quarantine");

    /// <summary>执行选项（休眠授权必须由界面显式传入，默认 false）。</summary>
    public ExecutionOptions BuildExecutionOptions(bool authorizeHibernate) =>
        new(Settings.QuarantineBasePath, Settings.RetentionDays) { AuthorizeHibernate = authorizeHibernate };

    /// <summary>
    /// 由扫描报告生成清理计划（清单的数据形态）。
    /// 勾选状态**默认取条目自己的 DefaultChecked**（保守原则），可用参数覆盖；不记忆上次勾选。
    /// </summary>
    public CleanPlan BuildPlan(ScanReport scan, ISet<string>? checkedItemIds = null)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var items = new List<CleanPlanItem>();

        foreach (var entry in scan.Entries)
        {
            var definition = entry.Item;

            // 有内容的文件型条目，或命令型条目（休眠/DISM，它们没有文件）才进清单
            var actionable = entry.HasContent
                             || definition.ActionKind is CleanActionKind.HibernateOff or CleanActionKind.DismComponentCleanup;

            if (!entry.Available || !actionable)
            {
                continue;
            }

            var userChecked = checkedItemIds is null
                ? definition.DefaultChecked
                : checkedItemIds.Contains(definition.Id);

            items.Add(new CleanPlanItem(
                definition.Id,
                definition.Category,
                definition.DisplayName,
                ManifestWriter.ActionText(definition.ActionKind),
                entry.Files,
                entry.TotalBytes,
                definition.DefaultChecked,
                userChecked)
            {
                ActionNote = definition.ActionNote,
                RestoreHint = definition.RestoreHint,
                SideEffect = definition.SideEffect,
                Risk = definition.Risk,
                ActionKind = definition.ActionKind,
                Kept = Array.Empty<ScanFile>(),
                UnavailableReason = entry.UnavailableReason
            });
        }

        var requiredBytes = items
            .Where(i => i.UserChecked && i.ActionKind == CleanActionKind.Quarantine)
            .Sum(i => i.TotalBytes);

        var sameVolume = Volumes
            .GetVolumeOf(QuarantineRoot)
            .Equals(Volumes.GetVolumeOf(scan.Volume.Drive), StringComparison.OrdinalIgnoreCase);

        return new CleanPlan(
            Clock.Now.ToString("yyyyMMdd-HHmmss-fff"),
            Clock.Now,
            scan,
            items,
            requiredBytes,
            sameVolume);
    }

    /// <summary>
    /// 启动准备：日志滚动 → 隔离区账本自检 → 到期批次惰性释放 → 隔离区路径校验。
    /// 必须在界面出现之前调用一次（无常驻进程，"定时"语义只能落在这里）。
    /// </summary>
    public StartupPreparation Prepare()
    {
        LogHousekeeping.PruneOldLogs(Settings.LogDirectory, Settings.LogRetentionDays, Clock, FileSystem, Log);

        var recovery = Quarantine.Recover(QuarantineRoot);
        var release = Quarantine.ReleaseExpired(QuarantineRoot);

        var validation = PathValidator.Validate(
            Settings.QuarantineBasePath,
            requiredBytes: 0,
            sourceVolumeOf: Environment.SystemDrive + Path.DirectorySeparatorChar,
            volumes: Volumes,
            fileSystem: FileSystem,
            environment: Environment);

        return new StartupPreparation(recovery, release, validation.IsUsable, validation.Message);
    }

    /// <summary>按设置创建完整内核（界面与 CLI 的唯一入口）。</summary>
    public static CoreServices Create(
        AppSettings? settings = null,
        IFileSystem? fileSystem = null,
        IClock? clock = null,
        IVolumeProbe? volumes = null,
        IEnvironmentProbe? environment = null,
        ICommandRunner? commandRunner = null,
        ILogSink? log = null,
        IReadOnlyList<string>? otherDriveRoots = null)
    {
        var fileSystemInstance = fileSystem ?? new WindowsFileSystem();
        var clockInstance = clock ?? new SystemClock();
        var volumesInstance = volumes ?? new WindowsVolumeProbe();
        var environmentInstance = environment ?? new WindowsEnvironmentProbe();

        var settingsStore = new SettingsStore(log: log);
        var effectiveSettings = SettingsStore.Normalize(settings ?? settingsStore.Load());

        var logSink = log ?? new FileLogSink(effectiveSettings.LogDirectory, clockInstance);

        var recycleRoots = RecycleBinTargets.BuildRoots(
            environmentInstance,
            effectiveSettings.IncludeOtherDriveRecycleBin ? otherDriveRoots : null);
        var recycleBin = new RecycleBinTargets(fileSystemInstance, recycleRoots, logSink);

        return new CoreServices(
            effectiveSettings,
            settingsStore,
            logSink,
            fileSystemInstance,
            clockInstance,
            volumesInstance,
            environmentInstance,
            commandRunner ?? new ProcessCommandRunner(),
            recycleBin);
    }
}
