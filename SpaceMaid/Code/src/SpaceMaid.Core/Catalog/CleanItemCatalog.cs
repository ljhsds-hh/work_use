using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Catalog;

/// <summary>
/// 清理项闭集（需求 2.2/2.3/2.4/2.5，设计文档 §4.1）。
///
/// 三条不能破的规矩：
/// 1. **闭集**：只有登记在这里的路径才可能被扫描与清理；新增条目必须在评审中回答"删掉之后会发生什么"。
///    应用日志类条目一律**逐条**登记（<c>l1.app-logs-*</c>），不做"扫全盘 logs"式的通配。
/// 2. **Id 一经发布不得复用/改名**：隔离区映射表与导出的清单 csv 靠 Id 与用户的历史报告对应。
/// 3. **路径只写 <c>%VAR%</c> 模板**：源码里不出现盘符（否则换机/多用户环境会指向错误位置）。
///
/// 关于"中间夹着通配目录"的模板（例如 <c>%LOCALAPPDATA%\Packages\*\LocalCache\Temp</c>）：
/// <see cref="TargetRule.Path"/> 不允许出现通配符（<see cref="Safety.PathNormalizer"/> 会直接拒绝，
/// 因为落地路径必须唯一确定）。因此约定为：
/// <c>Path</c> = 最深的**固定**目录（枚举根），<c>Pattern</c> = 通配段对应的**子目录名**，
/// 由扫描/落地层按"枚举根的直接子目录名 == Pattern"再展开一层。
/// 这个约定是**失败关闭**的：即使展开逻辑尚未实现，也只会"扫不到"而绝不会多删。
///
/// <see cref="CleanItemDefinition.AutoRegenerated"/> 在本项目中是"允许默认勾选"的门槛字段（设计 §4.2-3）：
/// 只有 L1 全部条目与 <c>l2.windows-old</c> 为 true，其余一律 false（保守原则，需求 1.2-9 / 5.4-1）。
/// </summary>
public static class CleanItemCatalog
{
    private static readonly IReadOnlyList<CleanItemDefinition> Items = Build();
    private static readonly Dictionary<string, CleanItemDefinition> Index =
        Items.ToDictionary(item => item.Id, StringComparer.Ordinal);

    /// <summary>全部清理项（只读，顺序与需求 2.2/2.3/2.4/2.5 一致）。</summary>
    public static IReadOnlyList<CleanItemDefinition> All => Items;

    /// <summary>按 Id 精确查找（大小写敏感：清单与映射表都用逐字一致的 Id）；不存在返回 null。</summary>
    public static CleanItemDefinition? ById(string id) =>
        !string.IsNullOrEmpty(id) && Index.TryGetValue(id, out var item) ? item : null;

    private static IReadOnlyList<CleanItemDefinition> Build() => new[]
    {
        // ── L1 一键直清：删掉之后会被自动重建 / 系统本来就会自动清理（需求 2.2） ──
        L1(
            "l1.user-temp",
            "用户临时目录",
            "正在被占用的文件会被跳过并记录下来，其余临时文件先移入隔离区；这些文件本来由系统与软件临时生成，之后会自动重建，没有其他影响。",
            new[] { TargetRule.Contents(@"%TEMP%") }),

        L1(
            "l1.windows-temp",
            "系统临时目录",
            "正在被占用的文件会被跳过并记录下来，其余系统临时文件会被系统自动重建，不影响系统运行。",
            new[] { TargetRule.Contents(@"%SystemRoot%\Temp") }),

        L1(
            "l1.wu-download",
            "Windows 更新下载缓存",
            "已下载但还没安装的更新补丁需要重新下载；已经安装好的更新不受影响。",
            new[] { TargetRule.Contents(@"%SystemRoot%\SoftwareDistribution\Download") }),

        L1(
            "l1.delivery-optimization",
            "传递优化缓存",
            "传递优化缓存由系统自动重建；之后把更新共享给局域网内其他设备时会重新缓存一遍。",
            new[] { TargetRule.Contents(@"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization") }),

        L1(
            "l1.wer",
            "错误报告缓存",
            "只删除 Windows 错误报告的排队与归档记录，目录会被系统自动重建；已经上报出去的记录不受影响。",
            new[]
            {
                TargetRule.Contents(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"),
                TargetRule.Contents(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                TargetRule.Contents(@"%ProgramData%\Microsoft\Windows\WER\Temp")
            }),

        L1(
            "l1.cbs-logs",
            "系统安装与更新日志",
            "删除的是组件安装与更新留下的历史排障日志，系统会继续写新日志；不影响运行，只是回头查旧安装问题时少了一份记录。",
            new[]
            {
                TargetRule.Contents(@"%SystemRoot%\Logs\CBS"),
                TargetRule.Contents(@"%SystemRoot%\Logs\DISM"),
                TargetRule.Contents(@"%SystemRoot%\Logs\WindowsUpdate")
            }),

        L1(
            "l1.dumps",
            "内核与蓝屏转储",
            "保留最近一次蓝屏转储（最新的 MEMORY.DMP 或最新的一个 Minidump），更早的转储全部清除；LiveKernelReports 里的内核实时报告没有分析价值，全部清除。如果以后还要排查蓝屏，就只剩最近这一次现场可用了。",
            new[]
            {
                TargetRule.File(@"%SystemRoot%\MEMORY.DMP"),
                TargetRule.Glob(@"%SystemRoot%\Minidump", "*.dmp"),
                TargetRule.Contents(@"%SystemRoot%\LiveKernelReports")
            },
            keepsNewest: true),

        L1(
            "l1.thumb-cache",
            "缩略图与图标缓存",
            "这些缓存文件通常正被 explorer.exe（文件资源管理器）占用：被占用的文件会跳过并提示“重启资源管理器后可再次清理”，本工具不会自动结束你的 explorer.exe。清掉后首次浏览文件夹时缩略图会重新生成，会略慢一点。",
            new[]
            {
                TargetRule.Glob(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "thumbcache_*.db"),
                TargetRule.Glob(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "iconcache_*.db")
            }),

        L1(
            "l1.packages-temp",
            "应用包临时目录",
            "只处理各应用包目录下的 LocalCache\\Temp：这些都是应用自己生成的临时文件，应用会重新生成；你的应用数据与设置不在这里，不会被清掉。",
            new[] { new TargetRule { Kind = TargetKind.DirectoryContents, Path = @"%LOCALAPPDATA%\Packages", Pattern = "LocalCache" } }),

        L1(
            "l1.app-logs-vscode",
            "VS Code 日志缓存",
            "只删除 VS Code 的历史排障日志，下次启动会重新生成；你的设置、插件与代码都不受影响。",
            new[] { TargetRule.Contents(@"%APPDATA%\Code\logs") }),

        L1(
            "l1.app-logs-jetbrains",
            "JetBrains 系列 IDE 日志缓存",
            "只删除 JetBrains 系列 IDE 的日志目录，下次启动会重新生成；你的项目、设置与插件都不受影响。",
            new[] { new TargetRule { Kind = TargetKind.DirectoryContents, Path = @"%LOCALAPPDATA%\JetBrains", Pattern = "log" } }),

        // ── L2 推荐清理：用户数据或可再下载资源，绝大多数默认不勾（需求 2.3） ──
        L2(
            "l2.browser-cache",
            "浏览器缓存",
            ItemRisk.Safe,
            "只清理网页缓存文件，浏览器会重新缓存；不会删除密码、书签与历史记录（Cookie 与登录态是单独一项，默认不勾）。",
            new[]
            {
                TargetRule.Contents(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\GPUCache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\GPUCache"),
                new TargetRule { Kind = TargetKind.DirectoryContents, Path = @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles", Pattern = "cache2" }
            }),

        L2(
            "l2.browser-cookies",
            "浏览器站点数据（Cookie 与登录态）",
            ItemRisk.Caution,
            "会丢失所有网站的登录状态，之后需要重新登录，部分网站还要重新做一次验证码或短信验证；Cookie 属于用户数据而不是缓存，所以本项默认不勾。",
            new[]
            {
                TargetRule.File(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Network\Cookies"),
                TargetRule.File(@"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Network\Cookies"),
                TargetRule.Glob(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles", "cookies.sqlite")
            },
            actionNote: "会丢失网站登录态",
            restoreHint: "不可还原：Cookie 属于用户数据，只能重新登录各网站"),

        L2(
            "l2.downloads-installers",
            "下载目录中的安装包",
            ItemRisk.Caution,
            "只处理下载目录里超过 30 天没被修改过的安装包与压缩包（*.exe/*.msi/*.zip）；删除后需要重新下载，所以默认不勾，请先确认里面没有你还想留的安装包。",
            new[]
            {
                TargetRule.Contents(@"%USERPROFILE%\Downloads", "*.exe"),
                TargetRule.Contents(@"%USERPROFILE%\Downloads", "*.msi"),
                TargetRule.Contents(@"%USERPROFILE%\Downloads", "*.zip")
            },
            actionNote: "删除后需重新下载",
            restoreHint: "不可还原：只能回到原网站重新下载",
            minAge: TimeSpan.FromDays(30)),

        L2(
            "l2.dev-caches",
            "开发包管理器缓存",
            ItemRisk.Caution,
            "这些都是包管理器下载下来的依赖缓存（npm/pnpm/yarn/pip/NuGet/Gradle/Maven/Go/Cargo），删除后下次构建或安装依赖需要重新下载，耗时且依赖网络；你的项目源码与配置文件不在其中。",
            new[]
            {
                TargetRule.Contents(@"%LOCALAPPDATA%\npm-cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\pnpm\store"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Yarn\Cache"),
                TargetRule.Contents(@"%LOCALAPPDATA%\pip\Cache"),
                TargetRule.Contents(@"%USERPROFILE%\.nuget\packages"),
                TargetRule.Contents(@"%LOCALAPPDATA%\NuGet\v3-cache"),
                TargetRule.Contents(@"%USERPROFILE%\.gradle\caches"),
                TargetRule.Contents(@"%USERPROFILE%\.m2\repository"),
                TargetRule.Contents(@"%LOCALAPPDATA%\go-build"),
                TargetRule.Contents(@"%USERPROFILE%\go\pkg\mod"),
                TargetRule.Contents(@"%USERPROFILE%\.cargo\registry")
            },
            actionNote: "下次构建需重新下载依赖",
            restoreHint: "不可还原：依赖会在下次构建时重新下载（需要联网）"),

        L2(
            "l2.windows-old",
            "旧系统残留",
            ItemRisk.Caution,
            "删除后会失去回退到升级前系统版本的能力；而系统的回退窗口本身只有 10 天，超过 10 天后 Windows 自己也会把它清掉。升级距今不足 10 天时本项默认不勾，等回退窗口自己关闭更省心。",
            new[]
            {
                TargetRule.Tree(@"%SystemDrive%\Windows.old"),
                TargetRule.Tree(@"%SystemDrive%\$WINDOWS.~BT"),
                TargetRule.Tree(@"%SystemDrive%\$WINDOWS.~WS")
            },
            defaultChecked: true,
            autoRegenerated: true,
            actionNote: "删除后无法回退到升级前系统版本",
            restoreHint: "不可还原：回退窗口本身只有 10 天，删除后无法再回到升级前的系统版本"),

        L2(
            "l2.driver-downloader",
            "显卡驱动安装包残留",
            ItemRisk.Safe,
            "只删除驱动安装器留下的下载缓存；下次更新驱动时需要重新下载驱动包，已经装好的驱动不受影响。",
            new[]
            {
                TargetRule.Contents(@"%ProgramData%\NVIDIA Corporation\Downloader"),
                TargetRule.Contents(@"%ProgramData%\NVIDIA Corporation\NV_Cache"),
                TargetRule.Contents(@"%ProgramData%\AMD\Downloader")
            }),

        L2(
            "l2.prefetch",
            "预取文件",
            ItemRisk.Safe,
            "预取记录会被清空，接下来几次开机会略慢、常用程序首次启动也会慢一些；Windows 会随着使用逐步重建这些文件。正因为会短暂影响开机速度，本项默认不勾。",
            new[] { TargetRule.Contents(@"%SystemRoot%\Prefetch") }),

        L2(
            "l2.crash-dumps",
            "浏览器与应用的崩溃转储",
            ItemRisk.Safe,
            "崩溃转储只在事后排查崩溃原因时有用，删除后对应软件的那次崩溃现场记录就没有了；不影响这些软件正常运行。",
            new[]
            {
                TargetRule.Contents(@"%LOCALAPPDATA%\CrashDumps"),
                TargetRule.Contents(@"%LOCALAPPDATA%\Google\CrashReports")
            }),

        // ── L3 谨慎清理：可能影响系统功能或不可逆，一律默认不勾 + 二次确认（需求 2.4） ──
        L3(
            "l3.hibernate",
            "休眠文件",
            ItemRisk.Dangerous,
            "执行的是 powercfg /h off：关闭休眠功能并释放 hiberfil.sys，不会直接删除任何文件。会连带关掉快速启动，开机慢几秒到十几秒，笔记本合盖久放会掉电；不影响睡眠与正常关机。随时可以用 powercfg /h on 把休眠与快速启动恢复回来。",
            Array.Empty<TargetRule>(),
            actionKind: CleanActionKind.HibernateOff,
            actionNote: "仅关闭功能，不删文件",
            restoreHint: "恢复：powercfg /h on"),

        L3(
            "l3.component-store",
            "组件存储清理",
            ItemRisk.Caution,
            "只走 DISM 官方命令（StartComponentCleanup），移除的是已经被新版取代的旧组件，不会直接删除 WinSxS 里的文件；耗时可能长达十几分钟，中途不要关机。清理后最近安装的更新可能无法卸载回滚，本工具不提供重置基线（ResetBase）选项。",
            Array.Empty<TargetRule>(),
            actionKind: CleanActionKind.DismComponentCleanup,
            actionNote: "只走 DISM 官方命令，耗时可能十几分钟",
            restoreHint: "不可还原：已被取代的旧组件移除后无法恢复；后续更新仍可正常安装"),

        L3(
            "l3.orphan-app-dirs",
            "卸载残留目录",
            ItemRisk.Dangerous,
            "只在确认注册表里已经没有任何卸载项指向该目录时才列出（判定不确定的一律不列出），所以清单通常很短。查找范围只有 ProgramData、本地 AppData、漫游 AppData 三处，不会进入 Program Files；里面可能还留着旧软件的配置，保留期内可以从隔离区还原。",
            new[]
            {
                TargetRule.Contents(@"%ProgramData%"),
                TargetRule.Contents(@"%LOCALAPPDATA%"),
                TargetRule.Contents(@"%APPDATA%")
            },
            actionNote: "只在确认注册表已无对应卸载项时才列出",
            restoreHint: "可从隔离区还原（保留期内）；还原后它仍是一个没人引用的残留目录"),

        L3(
            "l3.large-files",
            "大文件",
            ItemRisk.Caution,
            "本项只按体积把大文件排出来给你看（默认取最大的约 200 个），工具不做自动勾选、也不替你判断哪个能删；里面可能有你的项目文件、虚拟机镜像或视频素材，请逐条确认后再勾。",
            CommonUserDataTargets(),
            actionNote: "由你逐条勾选，工具不做自动判定",
            restoreHint: "可从隔离区还原（保留期内）；保留期结束后不可还原"),

        L3(
            "l3.duplicate-files",
            "重复文件",
            ItemRisk.Caution,
            "工具只按文件内容把疑似重复的文件分组列出（每组默认保留创建最早的那一份），删哪一份完全由你决定；不同目录下的同名文件有时确实需要各留一份，请不要不看内容就整组勾选。",
            CommonUserDataTargets(),
            actionNote: "由你逐条勾选，工具不做自动判定",
            restoreHint: "可从隔离区还原（保留期内）；保留期结束或清空隔离区后不可还原"),

        L3(
            "l3.chat-cache",
            "聊天工具文件缓存",
            ItemRisk.Dangerous,
            "这些目录里极可能包含你要保留的聊天文件、图片与视频（微信的 FileStorage、Image、Video 都在其中）；删除后聊天软件里的旧文件需要重新从服务器或对方重新下载，对方已经不在线时可能再也拿不回来。所以默认不勾，请先打开目录确认。",
            new[]
            {
                TargetRule.Contents(@"%USERPROFILE%\Documents\WeChat Files"),
                TargetRule.Contents(@"%USERPROFILE%\Documents\Tencent Files"),
                TargetRule.Contents(@"%APPDATA%\Tencent")
            },
            actionNote: "可能包含你要保留的聊天文件",
            restoreHint: "可从隔离区还原（保留期内）；清除后旧文件需重新从服务器或对方下载"),

        L3(
            "l3.pagefile",
            "页面文件",
            ItemRisk.Caution,
            "页面文件由系统自己管理，本项只展示它占了多少空间，不会执行任何清理动作；确实想调整的话，请在系统设置的虚拟内存里自行修改（改完需要重启）。",
            new[] { TargetRule.File(@"%SystemDrive%\pagefile.sys") },
            actionKind: CleanActionKind.InformationalOnly,
            actionNote: "仅展示，不可清理",
            restoreHint: "不需要恢复：本项不会执行任何清理动作"),

        // ── 回收站单列一档（需求 2.5） ──
        new CleanItemDefinition
        {
            Id = "rb.recycle-bin",
            Category = CleanCategory.RecycleBin,
            DisplayName = "回收站",
            Risk = ItemRisk.Caution,
            ActionKind = CleanActionKind.Quarantine,
            Targets = new[] { new TargetRule { Kind = TargetKind.RecycleBin, Path = @"%SystemDrive%\" } },
            ActionNote = "保留期内可还原，清空隔离区后不可还原",
            SideEffect = "本次只处理系统盘上的回收站，并按 SID 子目录成对处理 $I 元数据与 $R 实体文件（不整目录删除）。里面的东西可能是你手滑删掉、还想找回来的，所以默认不勾；同样先移入隔离区，保留期内可以还原。",
            RestoreHint = "可从隔离区还原（保留期内）；保留期结束或手动清空隔离区后不可还原",
            DefaultChecked = false,
            AutoRegenerated = false
        }
    };

    /// <summary>L1：系统/软件自己生成、删掉会自动重建 => AutoRegenerated 与 DefaultChecked 均为 true。</summary>
    private static CleanItemDefinition L1(
        string id,
        string displayName,
        string sideEffect,
        IReadOnlyList<TargetRule> targets,
        bool keepsNewest = false) => new()
        {
            Id = id,
            Category = CleanCategory.L1OneClick,
            DisplayName = displayName,
            Risk = ItemRisk.Safe,
            ActionKind = CleanActionKind.Quarantine,
            Targets = targets,
            SideEffect = sideEffect,
            DefaultChecked = true,
            AutoRegenerated = true,
            KeepsNewest = keepsNewest
        };

    /// <summary>L2：默认不勾；只有 l2.windows-old 例外（系统自己会在 10 天后清理）。</summary>
    private static CleanItemDefinition L2(
        string id,
        string displayName,
        ItemRisk risk,
        string sideEffect,
        IReadOnlyList<TargetRule> targets,
        bool defaultChecked = false,
        bool autoRegenerated = false,
        string actionNote = "",
        string restoreHint = "",
        TimeSpan? minAge = null) => new()
        {
            Id = id,
            Category = CleanCategory.L2Recommended,
            DisplayName = displayName,
            Risk = risk,
            ActionKind = CleanActionKind.Quarantine,
            Targets = targets,
            SideEffect = sideEffect,
            ActionNote = actionNote,
            RestoreHint = restoreHint,
            DefaultChecked = defaultChecked,
            AutoRegenerated = autoRegenerated,
            MinAge = minAge
        };

    /// <summary>L3：一律默认不勾并带动作性质与恢复方式；命令型动作（休眠/DISM）不需要文件目标。</summary>
    private static CleanItemDefinition L3(
        string id,
        string displayName,
        ItemRisk risk,
        string sideEffect,
        IReadOnlyList<TargetRule> targets,
        CleanActionKind actionKind = CleanActionKind.Quarantine,
        string actionNote = "",
        string restoreHint = "") => new()
        {
            Id = id,
            Category = CleanCategory.L3Cautious,
            DisplayName = displayName,
            Risk = risk,
            ActionKind = actionKind,
            Targets = targets,
            SideEffect = sideEffect,
            ActionNote = actionNote,
            RestoreHint = restoreHint,
            DefaultChecked = false,
            AutoRegenerated = false
        };

    /// <summary>
    /// 大文件 / 重复文件只做展示的候选范围：用户目录的**直接内容**（非递归），
    /// 由用户逐条勾选（需求 4.1-6 明令"不递归删除用户目录"）。
    /// </summary>
    private static IReadOnlyList<TargetRule> CommonUserDataTargets() => new[]
    {
        TargetRule.Contents(@"%USERPROFILE%\Downloads"),
        TargetRule.Contents(@"%USERPROFILE%\Documents"),
        TargetRule.Contents(@"%USERPROFILE%\Desktop"),
        TargetRule.Contents(@"%USERPROFILE%\Pictures"),
        TargetRule.Contents(@"%USERPROFILE%\Videos")
    };
}
