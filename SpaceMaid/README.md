# SpaceMaid C 盘空间管家

把系统盘（C 盘）上"该清的垃圾"安全地清掉，把"可能是垃圾的东西"摆到你面前由你自己决定。

SpaceMaid 只做一件事：**C 盘空间清理**。它不碰注册表、不做系统优化、不联网、无广告、无遥测，也不常驻后台——打开就用、用完就关。

设计上最核心的一条：**清理不等于删除**。所有分级（含 L1 与回收站）清理的对象都先**移入隔离区**，保留期内随时可以一键还原。

> 需求基线：[Docs/需求.md](Docs/需求.md) v0.7（已冻结，两轮审核通过）。

## 能清理什么

清理项按"删掉之后会发生什么"分四档（不是按目录分档）。所有条目登记在内核的闭集 `CleanItemCatalog` 中，清单之外的路径一律不碰。

### L1 一键直清（默认全部勾选，跟随"一键清理"批量执行）

判据：系统或软件自己生成的纯缓存/临时数据，删掉之后会被自动重建，没有副作用。**仍然先移入隔离区**，保留期内可还原。

| 项目 Id | 名称 |
| --- | --- |
| `l1.user-temp` | 用户临时目录（`%TEMP%`） |
| `l1.windows-temp` | 系统临时目录（`%SystemRoot%\Temp`） |
| `l1.wu-download` | Windows 更新下载缓存（`SoftwareDistribution\Download`） |
| `l1.delivery-optimization` | 传递优化缓存 |
| `l1.wer` | 错误报告缓存（`WER\ReportQueue`、`ReportArchive`、`Temp`） |
| `l1.cbs-logs` | 系统安装与更新日志（`Logs\CBS`、`Logs\DISM`、`Logs\WindowsUpdate`） |
| `l1.dumps` | 内核与蓝屏转储（**保留最近一次**） |
| `l1.live-kernel-reports` | 内核实时报告（`LiveKernelReports` 全清） |
| `l1.thumb-cache` | 缩略图与图标缓存（被 explorer 占用则跳过） |
| `l1.packages-temp` | 应用包临时目录（`Packages\*\LocalCache\Temp`） |
| `l1.app-logs-vscode` | VS Code 日志缓存 |
| `l1.app-logs-jetbrains` | JetBrains 系列 IDE 日志缓存 |

> L1 清单是**闭集**。新增任何 L1 项，都必须在评审中回答"删掉之后会发生什么"，只有答案是"自动重建、无副作用"才允许进入。应用日志类条目一律**逐条**登记（`l1.app-logs-*`），不做"扫全盘 logs"式的通配。

### L2 推荐清理（默认不勾，你可自行勾选）

判据：属于你的数据或可再下载的资源，删了要重新下载/重新登录/重新缓存。**绝大多数项默认不勾**（保守原则），只有"系统本来就会自动清理"的项才默认勾选（当前仅 `l2.windows-old` 一项）。

| 项目 Id | 名称 | 默认勾选 |
| --- | --- | --- |
| `l2.browser-cache` | 浏览器缓存（Edge / Chrome / Firefox，不含 Cookie） | 否 |
| `l2.browser-cookies` | 浏览器站点数据（Cookie 与登录态） | 否（勾选会丢登录态） |
| `l2.downloads-installers` | 下载目录中的安装包（超过 30 天的 `*.exe` / `*.msi` / `*.zip`） | 否 |
| `l2.dev-caches` | 开发包管理器缓存（npm / pnpm / yarn / pip / NuGet / Gradle / Maven / Go / Cargo） | 否 |
| `l2.windows-old` | 旧系统残留（`Windows.old`、`$WINDOWS.~BT`、`$WINDOWS.~WS`） | 是（升级不足 10 天时自动改为不勾） |
| `l2.driver-downloader` | 显卡驱动安装包残留（NVIDIA / AMD） | 否 |
| `l2.prefetch` | 预取文件（`%SystemRoot%\Prefetch`） | 否 |
| `l2.crash-dumps` | 浏览器与应用的崩溃转储 | 否 |

### L3 谨慎清理（默认不勾，执行前二次确认）

判据：可能影响系统功能，或删除后不可逆。每项都写明了动作性质与恢复方式。

| 项目 Id | 名称 | 动作性质 |
| --- | --- | --- |
| `l3.hibernate` | 休眠文件 | **仅关闭功能，不删文件**（执行 `powercfg /h off`，可随时恢复） |
| `l3.component-store` | 组件存储清理 | 只走 DISM 官方命令（只移除已被取代的旧组件），耗时可能十几分钟 |
| `l3.orphan-app-dirs` | 卸载残留目录 | 只在确认注册表已无对应卸载项时才列出 |
| `l3.large-files` | 大文件 | 由你逐条勾选，工具不做自动判定 |
| `l3.duplicate-files` | 重复文件 | 由你逐条勾选，工具不做自动判定 |
| `l3.chat-cache` | 聊天工具文件缓存 | 可能包含你要保留的聊天文件 |
| `l3.pagefile` | 页面文件 | **仅展示，不可清理**（信息项） |

### 回收站（单列一档，默认不勾）

| 项目 Id | 名称 | 说明 |
| --- | --- | --- |
| `rb.recycle-bin` | 回收站 | 默认只处理 **C 盘**；按 SID 子目录成对处理 `$I`（元数据）与 `$R`（实体文件），不整目录删除 |

## 安全边界（永久不碰的东西）

以下几类对象在产品层面就是红线：任何版本、任何开关、任何"高级模式"都不允许绕过。禁止清单以**代码常量**硬编码在内核里，不是配置文件。

| 边界 | 具体内容 |
| --- | --- |
| 系统还原点 / 卷影副本（VSS） | **不扫描、不展示、不删除**。连"展示占用体积"也不做；代码库中不存在 `vssadmin` / `DeleteShadow` / `DeleteSnapshot` 调用路径（静态检索测试守住） |
| 系统目录树 | `System32`、`SysWOW64`、`WinSxS`、`Installer`、`Fonts`、`assembly`、`servicing`、`Microsoft.NET`、`System32\DriverStore`、`System32\config`、`System32\catroot`、`System32\spool`、`System32\winevt`、`System32\LogFiles`、`Boot`、`SystemResources`、`Recovery` |
| 正常安装目录 | `C:\Program Files`、`C:\Program Files (x86)` |
| 受保护的系统文件 | `hiberfil.sys`（只能通过 `powercfg /h off` 释放）、`pagefile.sys`、`swapfile.sys`、`DumpStack.log`、`DumpStack.log.tmp` |
| 用户目录根本身 | 用户配置文件根、桌面、文档、图片、视频、音乐、`AppData`、`LocalAppData`、`Downloads`、`OneDrive` 的**目录根**不可作为清理目标（其下具体文件仍可由明确条目处理，且不允许整目录递归） |
| 凭据与版本库 | 路径中任何一段出现 `.ssh`、`.git`、`.gnupg`、`System Volume Information` 即拒绝 |
| 链接与占用 | 路径自身或任一祖先目录是符号链接/junction 则拒绝；正在被占用的文件则跳过（**不自动结束你的 explorer.exe**） |
| 无绕过开关 | 不存在"强制清理/跳过确认/忽略禁止清单"这类参数或配置项 |

另外三条产品级约束：

1. **不碰注册表**：注册表清理风险高、收益低，明确排除（卸载残留项只**读**注册表做判定，不写不删）。
2. **只处理 C 盘**：不扫描也不清理其他分区；回收站默认也只处理 C 盘一个。
3. **无永久删除路径**：除隔离区到期释放外，工具不存在任何直接永久删除的代码路径。

## 使用流程

SpaceMaid 采用**人审阅、人执行、工具复核**的协作方式（需求 3.9）。工具不会在未经你审阅的情况下自行删除任何东西。

```
扫描（只读）
  -> 导出清单（只读，绝不删）
    -> 你审阅 清单.md
      -> 你点击执行清理（明确的人工动作，与导出之间没有自动衔接）
        -> 工具重新扫描并输出 复核报告.md
          -> 复核报告确认清单内项目全部按预期处理 = 审核完成
```

| 环节 | 发生什么 | 产物 |
| --- | --- | --- |
| 1. 扫描 | 只读枚举与统计，不写、不改名、不改属性；可随时中断，中断后已扫描部分仍有效 | 界面上的分级清单与体积明细 |
| 2. 导出清单 | 把本次会被处理/移动的**全部路径**写成两份清单，导出过程零写操作 | `清单.md`（给人看）+ `清单.csv`（给机器复核，逐文件穷尽） |
| 3. 你审阅 | 查看 `清单.md`，有异议就调整勾选或排除项后重新导出 | - |
| 4. 你执行 | 由你在界面里点击执行；一次执行 = 一个隔离批次 | 执行结果页（已处理量、跳过项、可还原项） |
| 5. 复核 | 重新扫描同一批清理项，与执行前清单逐条比对 | `复核报告.md`：已清理 / 未清理及原因 / **异常项逐条列出** |

清单与复核报告默认写到 `D:\logs\SpaceMaid\清单\<yyyyMMdd-HHmmss>\`（目录不可写时回退到 `%LocalAppData%\SpaceMaid\reports\`）。

扫描采用**流式枚举**（`IFileSystem.EnumerateFilesStreaming`，惰性、可取消、防目录环），因此不会为了"先数清再扫描"把整棵树物化成列表。同时每个清理项都有**单项界限**（`ScanEngine.MaxFilesPerItem = 20000` 个文件、`DefaultItemTimeBudget = 15 s`）：命中界限时不会当成失败，而是**取已收集的部分照常处理，并在清单里如实写明"本次结果可能不完整"**。这一条有实测依据——`%LOCALAPPDATA%\pnpm\store` 这类内容寻址缓存可达十万级小文件，加上界限之前光是它一项就要跑 20 s 以上，整机扫描能拖到十分钟还出不了结果。

### 只读清单不等于执行

**导出清单与执行清理之间不存在自动衔接**：清单是执行的输入，执行器只处理清单里的条目（`CleanExecutor` 从不自行枚举目录），因此不可能出现"清单里没有、执行时却删了"的项。工具也不提供"跳过清单直接清理""静默后台清理""定时自动清理"这类能力。

### 只读命令行（`--dry-run` / `--report`）

`SpaceMaid.exe` 自带两条**只读**命令，用于无人值守的批量复核。参数实现见 `Code/src/SpaceMaid.Core/Cli/CliOptions.cs`：

```
SpaceMaid                      打开界面（清理动作必须在这里经你确认后执行）
SpaceMaid --dry-run [--out 目录]   只扫描并导出清单（md + csv），不做任何删除
SpaceMaid --report 清单目录       对已有清单做执行后复核，输出复核报告
SpaceMaid --help                  显示本帮助（等价写法：-h、/?）
```

| 要点 | 说明 |
| --- | --- |
| `--dry-run` | 扫描当前机器并导出清单，**零删除**；`--out` 缺省时用设置里的报告目录（默认 `D:\logs\SpaceMaid\清单`） |
| `--report` | 必须跟一个清单目录参数，读回该清单并与现状比对，输出 `复核报告.md` |
| `--dry-run` 与 `--report` | 不能同时使用，同时给会按用法错误拒绝 |
| 退出码 | `0` = 成功；`1` = 执行失败；`2` = 用法错误（识别不了的参数、缺值、或用了被拒绝的开关） |
| 不存在的删除开关 | `--clean` / `--delete` / `--remove` / `--execute` / `--force` / `--yes` / `-y` / `--all` / `--silent` 一律以退出码 2 拒绝，并提示"本工具不提供命令行清理能力（xxx）：清理动作必须由你在界面里确认后执行" |
| 绝不回落到 GUI | 只要带了参数就走命令分支，参数不合法直接报错退出——**不会**静默变成"打开工具" |

> **UAC 提醒**：`SpaceMaid.exe` 的清单固定 `requireAdministrator`，所以 `SpaceMaid.exe --dry-run` 也会先弹一次 UAC。无人值守时可以直接用 dotnet 宿主加载：`dotnet SpaceMaid.dll --dry-run`（Debug 产物目录下），这样不触发提权提示。

实测一轮（本机 Debug 产物，未提权会话，本轮文档核验时实跑）：`dotnet SpaceMaid.dll --dry-run` 退出码 0，耗时 **2.2 s**，报出 **19 个可用清理项**，清单落在 `D:\logs\SpaceMaid\清单\<时间戳>\`，头部形如：

```
- 系统盘：C:\（总容量 199.06 GB，可用 64.53 GB）
- 本次计划处理：39158 个文件，共 6.32 GB
- 仅展示、不执行：页面文件 15 GB（不计入上面的可处理体积）
- 隔离区口径：与源文件同卷——清理后 C 盘空间不会立刻释放
```

（文件数与体积随机器状态每时每刻在变，上面是某一次实际输出的样本，以你那次清单头部为准。）

注意这里的**体积口径**：`页面文件` 是"只展示、不可清理"的信息项，它占空间但**永远不执行**，所以清单把它单列一行并明确不计入"计划处理"。**信息项的体积混进"可处理体积"就是虚报**——同一台机器上，把页面文件算进去的口径比正确口径多出约 15 GB（`C:\pagefile.sys`）。

## 隔离区

隔离区是本工具自己的暂存目录。**删除动作的终点是隔离区，不是永别。**

| 项目 | 说明 |
| --- | --- |
| 路径可自定义 | 设置页可指向任意本地目录；默认 `%LocalAppData%\SpaceMaid\Quarantine`。实际存放结构固定挂在 `<你选的目录>\SpaceMaid\Quarantine` 下，不污染你的目录结构 |
| 路径校验 | 选定即校验，不通过不允许保存：网络路径/映射盘拒绝、不可写拒绝、系统目录与磁盘根拒绝、跨卷空间不足拒绝、可移动介质警告 |
| 默认保留期 | 7 天（可设置 1 至 365 天） |
| 组织结构 | 按 `<隔离区>\<yyyyMMdd-HHmmss-fff>\` 分批；每批一份 `map.json` 映射表（原始绝对路径、所在卷、体积、时间、所属分级）是还原的**唯一依据**；实体文件放在 `payload\` 子目录 |
| 断电安全 | 两阶段写盘：先写 `pending=true` 的完整账本，再搬文件，最后改写为已完成。绝不出现"文件搬了但没记账"，程序异常退出后重启自检即可修复账目 |
| 同卷不立刻释放空间 | **隔离区与源文件在同一卷时（例如都在 C 盘），移动只是换个目录，C 盘占用不会立刻下降。** 要等保留期结束或你手动清空隔离区后才真正释放。界面会如实显示"已移入隔离区 X GB，尚未释放"，并建议把隔离区改到其他盘 |
| 到期释放是惰性的 | 本工具**无常驻进程、不注册计划任务**，所以"到期自动释放"实际发生在下一次启动或下一次扫描时。启动时会提示"隔离区有 X GB 已到期，可释放" |
| 一键还原 | 按映射表把文件搬回原位置；原位置已存在同名文件/原卷不可用/空间不足时**逐条报告冲突，绝不静默覆盖** |
| 立即清空 | 随时可清空整个隔离区（需二次确认，文案写明**不可还原**） |

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1809（Redstone 5）及以上 / Windows 11，x64 |
| 运行时 | 无需安装，Release 为单文件自包含发布（已内置 .NET 8 运行时） |
| 权限 | **必须管理员权限**。`SpaceMaid.exe` 的清单固定 `requireAdministrator`，双击即弹一次 UAC。原因是要清理系统临时目录与更新缓存；被以普通权限拉起时程序会拒绝进入清理流程（弹出"权限不足"并阻断），全流程不会再出现第二次提权提示。只读命令 `--dry-run` / `--report` 走的是同一个 exe，所以同样会先弹一次 UAC |
| 磁盘 | 需要 C 盘（系统盘）；建议把隔离区放在非系统盘，这样清理才会立刻腾出 C 盘空间 |
| 网络 | 全程不需要联网，也不产生任何网络请求 |

## 构建与发布

依赖 .NET 8 SDK。

```bash
# 构建
dotnet build Code/space-maid.slnx

# 运行测试（用例数随开发变动，一律以本次输出为准）
dotnet test Code/space-maid.slnx

# 只跑安全底座的用例
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Safety

# 重新生成应用图标（产出 Assets/app.ico，9 种尺寸 16/20/24/32/40/48/64/128/256，勿手工编辑）
# 本机执行策略禁止直接运行 .ps1，所以在仓库根目录这样调用：
Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Code\scripts\gen-icon.ps1')

# 发布免安装单文件 EXE（Release 配置下自动启用自包含 + 单文件 + 压缩）
dotnet publish Code/src/SpaceMaid.App -c Release
```

发布产物：`Code/src/SpaceMaid.App/bin/Release/net8.0-windows/win-x64/publish/SpaceMaid.exe`，实测 **67,217,692 字节（64.1 MB）**，且 **publish 目录下只有这一个文件**。

"publish 目录只剩一个 exe" 是靠 `Code/Directory.Build.props` 守住的：它只对 Release 设置 `DebugType=none` / `DebugSymbols=false`。原因是 `SpaceMaid.Core.pdb` 会跟着被拷进 publish 目录，让"一个 exe 免安装"这句话在目录列表里当场破功。放在解决方案级的 `Directory.Build.props` 而不是各 `csproj` 里，是为了以后新增工程时不会漏掉；**Debug 配置必须保留 pdb**，否则出问题没有行号可查。

打包、管理员清单、杀软误报与升级卸载的完整说明见 [Docs/发布部署指南.md](Docs/发布部署指南.md)。

## 目录结构

```
SpaceMaid/
├─ README.md                     本文件：使用说明与安全边界
├─ CLAUDE.md                     工程关键记忆（给 AI 迭代开发用）
├─ Docs/
│  ├─ 需求.md                    SRS 基线 v0.7（已冻结）
│  ├─ 设计文档.md                架构分层、安全不变量、模块设计
│  ├─ 实施计划.md                TDD 任务级实施计划（Task 1-15）
│  ├─ 测试报告.md                需求第 7 章 15 条验收标准逐条对照
│  ├─ 发布部署指南.md            单文件发布、UAC、升级卸载
│  └─ 评审记录.md                需求与代码评审台账
└─ Code/
   ├─ space-maid.slnx
   ├─ Directory.Build.props       解决方案级构建属性（仅 Release 去掉调试符号，保住"单文件"）
   ├─ scripts/
   │  ├─ gen-icon.ps1             程序化生成应用图标（产出 Assets/app.ico）
   │  └─ smoke-ui.ps1             界面冒烟脚本（未提权启动必须被正确阻断）
   ├─ src/
   │  ├─ SpaceMaid.Core/         清理内核（net8.0-windows，零 WPF 依赖）
   │  │  └─ Cli/                 只读命令行（CliOptions / CliRunner）
   │  └─ SpaceMaid.App/          WPF + HandyControl 界面（引用 Core）
   │     └─ Assets/app.ico       应用图标（由 gen-icon.ps1 生成，勿手工编辑）
   └─ tests/
      ├─ SpaceMaid.Core.Tests/   内核单测（主战场）
      └─ SpaceMaid.App.Tests/    ViewModel / 文案单测
```

内核 `SpaceMaid.Core` 按职责分包，不按技术分层：

| 包 | 职责 |
| --- | --- |
| `Models/` | 领域模型：`CleanItemDefinition`、`TargetRule`、`ScanEntry`、`CleanPlan`、`QuarantineMap` 等 |
| `Catalog/` | `CleanItemCatalog`（清理项闭集）+ `CatalogValidator`（清单自检规则） |
| `Cli/` | `CliOptions`（参数解析，拒绝一切删除开关）+ `CliRunner`（只读扫描出清单 / 读回清单出复核报告） |
| `Safety/` | `PathNormalizer`、`Denylist`、`SafetyGate`（唯一落地授权入口）、`TargetPathMatcher` |
| `Scanning/` | `ScanEngine`、`ScanningRules`（年龄过滤、保留最新一份、Windows.old 10 天窗口） |
| `Quarantine/` | `QuarantinePathValidator`、`QuarantineStore`、`QuarantineService`、`QuarantineMapStore` |
| `Execution/` | `CleanExecutor`、`HibernateGate`、`DismComponentCleanup`、`RecycleBinTargets` |
| `Reporting/` | `ManifestWriter`（清单 md/csv）、`ReviewReporter`（复核报告）、`VolumeTextFormatter` |
| `Settings/` | `SettingsStore`、`AppSettings` |
| `Logging/` | `FileLogSink`、`LogHousekeeping`（日志滚动，惰性清理） |
| `Abstractions/` | `IFileSystem`、`IClock`、`IVolumeProbe`、`IVolumeCapacityProbe`、`IEnvironmentProbe`、`ICommandRunner`、`ILogSink` |
| `Platform/` | 上述抽象在 Windows 上的生产实现 |

依赖方向固定为 `App -> Core`；Core 内部 `Models` 被其他包依赖，且**不得出现 `using System.Windows`**（由静态检索测试守住）。

## 数据与日志位置

| 数据 | 路径 |
| --- | --- |
| 用户设置 | `%AppData%\SpaceMaid\settings.json`（隔离区位置、保留天数、日志目录、回收站范围） |
| 日志 | `D:\logs\SpaceMaid\spacemaid-yyyyMMdd.log`，按日分文件，默认滚动保留 30 天（下次启动时惰性清理） |
| 清单与复核报告 | `D:\logs\SpaceMaid\清单\<时间戳>\` |
| 隔离区 | 用户自定义；默认实际存放于 `%LocalAppData%\SpaceMaid\Quarantine` |

设置文件读不到或读坏了都会回默认值，不打断启动。

## 当前实现状态

| 部分 | 状态 | 证据 |
| --- | --- | --- |
| 内核 `SpaceMaid.Core` | 已完成：安全底座、扫描、隔离区、执行、报告、特殊项、设置与日志 | `dotnet test Code/space-maid.slnx` 全绿（用例数随开发变动，以命令输出为准） |
| 界面 `SpaceMaid.App` | 已完成：三段式主窗 + 设置窗，`ViewModels/`、`Views/`、`Themes/DesignTokens.xaml` 全部就位 | `Code/scripts/smoke-ui.ps1` 实测 PASS |
| 只读 CLI | **已接进 `SpaceMaid.exe`**：`App.xaml.cs` 解析命令行，`CliOptions.IsCommandLineInvocation` 为真时走 CLI 分支且**绝不回退到 GUI** | `dotnet SpaceMaid.dll --help` 退出码 0；`--clean` 退出码 2 并提示"本工具不提供命令行清理能力" |
| 应用图标 | 已就位：`Code/scripts/gen-icon.ps1` 程序化生成 9 种尺寸的 `Assets/app.ico`（381,038 字节，已逐像素验证） | 与 `SpaceMaid.App.csproj` 的 `<ApplicationIcon>` 一致 |
| 发布管道 | 已就位：`Code/Directory.Build.props` 仅对 Release 去掉调试符号 | `dotnet publish -c Release` 后 publish 目录只有 1 个文件 `SpaceMaid.exe`（67,217,692 字节） |
| 界面冒烟 | 已就位：`Code/scripts/smoke-ui.ps1`（用 dotnet 宿主加载，绕开 `requireAdministrator` 的 apphost，避免无人值守时卡在 UAC） | 实测 PASS：3 秒内匹配到标题"权限不足"，退出码 0，无进程残留 |

界面层的界面核验已经跑通，但**仍需人工过一遍端到端点击流程**；内核侧唯一待办是 `Code/tests/SpaceMaid.Core.Tests/Execution/DismComponentCleanupTests.cs`（实施计划点名的用例，目前 DISM 参数不含重置基线这条由 `HibernateGateTests` 的同名断言 + `Architecture/StaticSafetyTests.cs` 的源码级断言覆盖）。

> **仍未在真机执行过真实清理。** 按需求 3.9，工具只出清单，用户审阅后自己执行，工具再复核；到目前为止所有实测都停在 dry-run 与复核这一侧，"清理成功"没有任何实机证据。

## 文档索引

| 文档 | 内容 |
| --- | --- |
| [Docs/需求.md](Docs/需求.md) | 需求规格说明书 v0.7（基线，含 15 条验收标准） |
| [Docs/设计文档.md](Docs/设计文档.md) | 架构与实现设计、安全不变量 I-1 至 I-6、追溯矩阵 |
| [Docs/实施计划.md](Docs/实施计划.md) | TDD 任务级实施计划（Task 1-15 与检查点） |
| [Docs/测试报告.md](Docs/测试报告.md) | 验收标准逐条对照与测试运行方法 |
| [Docs/发布部署指南.md](Docs/发布部署指南.md) | 发布打包、UAC 清单、杀软误报、升级卸载、发布清单 |
| [Docs/评审记录.md](Docs/评审记录.md) | 需求评审与代码评审台账 |
| [CLAUDE.md](CLAUDE.md) | 工程关键记忆（需求决策、实现事实、验证方式） |

## 许可证

[The MIT License](../LICENSE)
