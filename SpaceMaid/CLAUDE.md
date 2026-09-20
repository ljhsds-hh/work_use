# SpaceMaid 工程关键记忆

## 项目定位

Windows 桌面 C 盘（系统盘）空间清理工具（work_use 工具集之一）：**分级清理 + 全量隔离区 + 清单审阅/复核流程**。

三条不可动摇的产品性质：

1. **清理不等于删除**：L1 / L2 / L3 / 回收站**全部**先移入隔离区，保留期内可一键还原（v0.6 用户拍板，L1 也不例外）；
2. **打开就用、用完就关**：不做托盘常驻、不开机自启、无后台服务、无定时清理。一切"定时"语义（隔离区到期释放、日志滚动）都降级为**下次启动惰性执行**；
3. **只出清单不删**：工具先出清单，用户审阅后自己执行，工具再复核出报告，这才算审核完成（需求 3.9）。

需求基线 `Docs/需求.md` v0.7（已冻结，两轮审核通过）。**改需求前先停下来问用户，不要自行改需求。**

## 关键需求决策（用户确认，勿擅自更改）

- 技术栈强制 **.NET 8（net8.0-windows）+ WPF + HandyControl 3.5.1**；UI 一律优先 HandyControl 控件，HC 没提供的才允许 WPF 原生控件，**禁止自绘伪控件**
- 内核 `SpaceMaid.Core` 独立类库、**零 WPF 依赖**，界面与内核解耦（设计决策 D-1）
- **三档分级 + 回收站单列**：L1 一键直清 / L2 推荐清理 / L3 谨慎清理 / 回收站。分档判据是"删掉之后会发生什么"，**不是"它在哪个目录"**
- **保守原则**：只有"删除后会被自动重建／系统本来就会自动清理"的项才允许**默认勾选**（当前仅 L1 全部 + `l2.windows-old`，且后者在升级不足 10 天时自动改为不勾）；其余一律默认不勾
- **不记忆勾选状态**：每次扫描都重置为 `DefaultChecked`，避免上次勾过的 L3 被自动沿用（需求 5.4-2）
- 隔离区路径**用户可自定义**（默认 `%LocalAppData%\SpaceMaid\Quarantine`），保留期默认 **7 天**；实际存放结构固定挂在 `<用户选的目录>\SpaceMaid\Quarantine` 下
- 权限模型：exe 清单固定 `requireAdministrator`（打开即一次 UAC），**不做按需提权、不常驻高权限服务**
- 内核转储：**保留最近一次**蓝屏转储（按 LastWrite 最新的 `MEMORY.DMP` 或最新的一个 Minidump），更早的清除；`LiveKernelReports` 全清
- 休眠文件：唯一执行方式是 `powercfg /h off`，**永不直接删除 `hiberfil.sys`**；必须先通过用户显式授权闸门
- 系统还原点 / 卷影副本：**永久禁止项**——不扫描、不展示、不删除，连体积也不展示
- 范围边界：**只处理 C 盘**；回收站默认也只处理 C 盘，其他盘需显式开启
- 应用图标由 `Code/scripts/gen-icon.ps1` 程序化生成（**该脚本尚未创建**，见"当前工程状态"）

## 安全不变量（唯一授权入口与硬约束）

对应设计文档 §3.1 的 I-1 至 I-6，任何改动都要先确认没有破坏它们。

| 编号 | 不变量 | 守住方式 |
| --- | --- | --- |
| I-1 | 任何文件移动/删除**必须**先经 `SafetyGate.Authorize(path, item)` 返回 `Allowed` | 静态检索测试断言删除 API 只出现在白名单文件里 |
| I-2 | 禁止清单是**代码常量**（`Denylist`），不是配置；命中即拒绝 | `SafetyGate` 保证判定顺序，`CatalogValidator` 断言目标根不在禁止树内 |
| I-3 | 清理项只能来自 `CleanItemCatalog`（闭集）；`DefaultChecked=true` 只允许自动重建判据 | `CatalogValidator` + 单测 |
| I-4 | `hiberfil.sys` 永远是禁止删除项；休眠只能经 `HibernateGate` 授权闸门 | `Denylist` 常量 + `HibernateGateTests`（未授权必须零命令调用） |
| I-5 | 系统还原点/VSS：无条目、无扫描、无展示；代码库不出现 `vssadmin` / `DeleteShadow` | `StaticSafetyTests` 源码级字符串断言 |
| I-6 | 不存在"绕过确认"的开关（无高级模式/强制参数） | `StaticSafetyTests.Core_should_not_offer_bypass_switches` |

### "先拒后允"的判定顺序不可调整

`SafetyGate.Authorize` 的顺序固定为（`Safety/SafetyGate.cs`）：

```
① 规范化失败        -> UnsafePath
② 禁止清单          -> DeniedByDenylist
③ 重解析点          -> ReparsePoint
④ 白名单子树        -> OutsideAllowlist
```

顺序是安全性质而不是风格问题：**即使某个清理项"允许"了某个系统目录，也永远越不过禁止清单**（`SafetyGateTests.Should_deny_even_when_item_allows` 就是这条的证据）。

配套细节：

- 重解析点检查会向上走祖先目录，代价高，因此 `SafetyGate` 内做了进程内缓存，上限 2 万条后整体清空；**检测抛异常时按"不安全"处理**（保守原则，宁可不删）
- `Denylist` 不能写成"整个 `C:\Windows`"——因为确实要清理 `C:\Windows\Temp`、`Logs\CBS`、`SoftwareDistribution\Download`、`Prefetch`、`MEMORY.DMP`、`Minidump`，所以逐条精确列举
- `Denylist` 区分三类判定：`IsDeniedTree`（目录树，含全部子路径）、`IsDenied`（合并判定，含精确文件、用户目录根本身、`.ssh`/`.git`/`.gnupg`/`System Volume Information` 段名）、`IsDeniedUserRoot`（**精确等于**用户目录根本身，其下具体文件仍可处理）

## 关键实现事实（易踩坑）

### 清理项闭集

- `CleanItemCatalog` 的**路径只写 `%VAR%` 模板**，源码里不出现盘符（否则换机/多用户环境全错）；`StaticSafetyTests.Catalog_should_not_hardcode_drive_letters` 用正则守住
- **Id 一经发布不得复用/改名**：隔离区映射表与导出的清单 csv 都靠 Id 与历史报告对应
- 中间夹着通配目录的模板（如 `%LOCALAPPDATA%\Packages\*\LocalCache\Temp`）不能把 `*` 写进 `TargetRule.Path`（`PathNormalizer` 会直接拒绝，因为落地路径必须唯一确定）。约定为：`Path` = 最深的**固定**目录（枚举根），`SubPathPattern` = 通配段对应的**子目录名**，由扫描/落地层按"枚举根的直接子目录名匹配模式"再展开一层。这个约定是**失败关闭**的：展开逻辑没实现也只会"扫不到"，绝不会多删
- `AutoRegenerated` 在本项目里是"**允许默认勾选**的门槛字段"：只有 L1 全部条目与 `l2.windows-old` 为 true
- 大文件 / 重复文件（`l3.large-files`、`l3.duplicate-files`）的候选范围是用户目录的**直接内容（非递归）**，对应需求 4.1-6"不递归删除用户目录"
- `l3.orphan-app-dirs` 的作用域**刻意收窄**到 `%ProgramData%`、`%LocalAppData%`、`%APPDATA%`，**不进入 `Program Files*`**：禁止清单把 Program Files 整棵树列为永不清除，为它开例外等于给禁止清单留绕过口子。要覆盖 Program Files 必须先改需求 4.2
- 卸载残留的列出条件是"**任一条件无法判定就不列出**"（同时满足：目录名不等于任何已安装程序、注册表卸载键里无指向该目录的 `InstallLocation`/`UninstallString`、目录根下有文件且最近 180 天无修改、不在 Denylist）

### 隔离区两阶段账本与批次 Id

- **两阶段写盘**（`QuarantineStore.StoreCore`）：① 先写完整账本并把全部条目标 `Pending` -> ② 再逐文件搬运 -> ③ 只保留真正搬进来的条目、`Pending=false` 重写。**绝不出现"文件搬了但没记账"**
- 搬运失败**一律保留源文件**，失败项不进账本，只出现在 `Skipped` 里（需求 3.4-3）
- **同卷 `File.Move`（原子重命名，不占额外空间）；跨卷"复制 -> 全量 SHA-256 校验 -> 删源"**，校验不一致就删掉不可信的副本、保留源文件
- **批次 Id 撞名 bug 与修复**（`QuarantineStore.AllocateBatch`）：批次 Id 精确到毫秒（`yyyyMMdd-HHmmss-fff`），**同一毫秒内连续清理两次（或时钟被回拨）会撞名，后一批的账本会覆盖前一批，直接导致文件丢失**。修复方式是分配批次目录时循环检测 `DirectoryExists`，撞名就加 `-1`、`-2` 后缀。**改动这段时不要退回"直接拼时间戳"的写法**
- 断电自检（`QuarantineStore.Recover`，启动时调用）按"文件到底在哪"修复仍标 `Pending` 的账目：副本在+源没了 -> 补记为已隔离；源还在 -> 丢弃该账目（**源文件是权威，用户的文件不丢**）；两边都在 -> 删掉多余副本并丢弃账目（**不谎报"已清理"**）；两边都没了 -> 标 `Unknown` 交给复核报告列为异常项
- **惰性释放**（`QuarantineService.ReleaseExpired`）：没有常驻进程、没有计划任务，所以只在 `CoreServices.Prepare()` 里（启动时）与扫描前各调用一次。"到期自动释放"是用户可见的说法，实现上是惰性的
- 删除类 API 的合法位置是固定白名单（`StaticSafetyTests.DeleteAllowlist`）：`Platform\WindowsFileSystem.cs`（`IFileSystem` 的唯一实现，所有删除最终收口到这里）、`Quarantine\QuarantineStore.cs`、`Quarantine\QuarantineService.cs`（还原失败时回滚刚写出的副本）、`Logging\LogHousekeeping.cs`（只删自家日志）。**新增删除调用点必须同时更新这个白名单并说明理由**
- `QuarantineStore.RemoveBatchDirectory` 有两重护栏：批次目录必须位于给定隔离区根之下、根目录自身绝不允许被删除

### 休眠授权闸门 / DISM / 回收站

- **未授权时一条命令都不发**（`HibernateGate.Disable(userAuthorized:false)` 立即返回 `Executed=false`，`Reason` 含"未授权"，且**绝不调用 `ICommandRunner`**）。`HibernateGateTests` 断言假 runner 调用次数为 0——这是需求第 7 章验收第 9 条的证据
- 授权后的固定动作是常量：`powercfg` + `/h off`（`PowerCfgExecutable` / `DisableArguments`，**不接受外部拼装**）。返回码非 0 判失败，**不重试、不降级为删文件**
- 休眠探测用 `powercfg /a` 的返回码判断系统是否支持，再用"`hiberfil.sys` 是否存在"判断是否已开启——后者与系统语言无关，比解析本地化输出可靠
- DISM 固定 `dism.exe /Online /Cleanup-Image /StartComponentCleanup`（`DismComponentCleanup.Arguments` 是常量），**不给重置基线开关**：加了它会让所有已安装更新都无法卸载回滚。超时按失败处理且不重试
- 回收站（`RecycleBinTargets`）默认只处理 `%SystemDrive%\$Recycle.Bin`；枚举各 SID 子目录，按后缀名把 `$I*`（元数据）与 `$R*`（实体文件）**成对**处理，两者都进隔离区才能在还原时成对放回；解析不了 `$I` 的孤儿 `$R` **一律跳过**（看不懂的东西不动）
- `$I` 元数据格式：8 字节版本 + 8 字节文件大小 + 8 字节删除时间；版本 >= 2 时自第 24 字节起是 4 字节路径字节长度、路径本体从偏移 28 开始（版本 1 从 24 开始），UTF-16LE 且以 `\0` 结尾

### 执行器与清单（D-7）

- `CleanExecutor.Execute` **只从 `plan.Checked` 的 `item.Files` 取文件，从不枚举目录**（设计决策 D-7），因此不可能出现"清单里没有、执行时却删了"的项
- 每个文件落地前先过 `SafetyGate.Authorize`，被拒的文件进 `Skipped` 并带原因；**取不到清理定义（没有白名单）就整项跳过**——没有白名单就不删，宁可什么都不做
- **隔离区路径预检在动任何文件之前**完成，不合格就整批中止（一个文件都不动），对应需求第 7 章验收第 7 条
- 一次执行 = 一个隔离批次
- 清单 csv 的**穷尽性**由测试断言（csv 数据行数 == 计划文件数）；`清单.md` 在单项目文件超过 50 个时只列前 50 并指向 csv
- 清单导出与执行之间**没有自动衔接**：导出只写清单文件，不碰任何被扫描目录

### 体积口径（D-6，最容易谎报的地方）

- `VolumeTextFormatter.DescribeProcessed(bytes, sameVolume)`：跨卷 -> `已释放 X GB`；同卷 -> `已移入隔离区 X GB（保留期结束或清空隔离区后释放）`
- **全量进隔离区之后，"清理了多少"和"真正释放了多少"是两个不同的数字。** 同卷场景任何地方都不许写成"已释放"。测试断言同卷文案**含**"已移入隔离区"且**不含**"已释放"
- 空间预检按"跨卷才需预留"的口径做：`QuarantinePathValidator` 只在跨卷时比较剩余空间，**同卷不因空间不足拒绝**

### 抽象层与 IO

- 文件系统、时钟、卷信息、命令执行、日志全部是接口（`IFileSystem` / `IClock` / `IVolumeProbe` / `IEnvironmentProbe` / `ICommandRunner` / `ILogSink`），生产实现在 `Platform/`，测试注入假实现或使用 `%TEMP%` 临时目录
- `ProcessCommandRunner.Run` 的**超时约定**：超时返回 `ExitCode == -1` 且 `StdErr` 含 `timeout`；实现上 `WaitForExit(timeout)` 后 `Kill(entireProcessTree: true)`
- JSON 统一走 `JsonSerializerOptions`（含 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，保证中文可读）+ **原子写**（写 `.tmp` -> `File.Replace`/`Move`）
- 设置（`AppSettings`）所有字段都有安全默认值：`QuarantineBasePath` 默认 `%LOCALAPPDATA%`、`RetentionDays` 默认 7（归一化到 1-365）、`LogDirectory` 默认 `D:\logs\SpaceMaid`、`ReportDirectory` 默认 `D:\logs\SpaceMaid\清单`、`LogRetentionDays` 默认 30、`IncludeOtherDriveRecycleBin` 默认 false。**读不到或读坏了都回默认值，不打断启动**
- 日志/报告/隔离区目录不可写时都要有回退路径，不能直接让功能挂掉

### 静态安全检索测试（`Code/tests/SpaceMaid.Core.Tests/Architecture/StaticSafetyTests.cs`）

需求里有几条不变量是**关于"不存在"的**（代码里不存在 VSS 删除路径、不存在重置基线开关、不存在绕过确认的开关），行为测试证明不了，只能在源码层立规矩。它读 `Code/src/SpaceMaid.Core/**/*.cs` 的**文本**做断言：

| 用例 | 断言 |
| --- | --- |
| `Core_should_not_reference_vss_or_shadow_copy_deletion` | 全文不含 `vssadmin` / `DeleteShadow` / `DeleteSnapshot` |
| `Core_should_not_use_component_store_reset_base` | 全文不含 `ResetBase`（裸字串、不区分大小写，见下方"已收紧"说明） |
| `Delete_calls_should_only_appear_in_allowlisted_files` | `File.Delete(` / `Directory.Delete(` / `.TryDeleteFile(` 只出现在白名单文件里 |
| `Core_should_not_depend_on_wpf` | 不含 `System.Windows` / `PresentationCore` |
| `Core_should_not_offer_bypass_switches` | 不含 `SkipConfirm` / `NoConfirm` / `ForceClean` / `ForceDelete` / `BypassSafety` / `IgnoreDenylist` / `AllowDenied` |
| `Hibernate_file_must_never_be_a_deletion_target` | `l3.hibernate` 的 `ActionKind == HibernateOff` 且 `Targets` 为空；全库没有 TargetRule 指向 `hiberfil`；`Denylist.IsDenied(hiberfil.sys)` 为 true |
| `Catalog_should_not_hardcode_drive_letters` | `CleanItemCatalog.cs` 里没有形如 `"X:\` 的硬编码盘符 |
| `Catalog_should_have_no_violations` | `CatalogValidator.Validate(CleanItemCatalog.All)` 返回空列表 |
| `Source_root_should_be_discovered` | 从 `AppContext.BaseDirectory` 向上能找到 `src/SpaceMaid.Core` |

**这条规则已于 2026-09-20 收紧（改动前必读）**：`Core_should_not_use_component_store_reset_base` 现在断言的是**裸字串** `ResetBase`（不区分大小写）在整个 Core 源码中都不出现——原来的带斜杠写法"看着通过"，却拦不住有人把开关当成参数拼进命令。为满足收紧后的规则，`l3.component-store` 的文案已改为「本工具不提供「重置基线」选项」，不再出现英文开关名。

如果以后确实需要在文案里重新写出这个开关名，**必须同时放宽这条断言**，并在评审记录里说明理由；但要先想清楚代价：写出开关名只对排障有用，而漏掉一次命令参数会让用户再也无法卸载已安装的更新。

> 注意：全局约束里的"**不得实现 `/ResetBase`**"指的是**实现**（命令参数），`DismComponentCleanup.Arguments` 常量里确实没有它，参数也不接受外部拼装。

## 验证方式

`dotnet test Code/space-maid.slnx`

**当前用例数（按源码统计，快照）= 288（内核）+ 23（界面）= 311**。统计口径是 `Σ [Fact] 个数 + Σ [InlineData] 个数`；**这个数字随开发快速变动，不要把它当固定事实，以 `dotnet test` 的实际输出为准**。复现命令见 `Docs/测试报告.md` 第 1.2 节。

常用过滤命令：

```bash
# 安全底座（路径规范化/禁止清单/唯一授权闸门）
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Safety

# 清单闭集与清单自检
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Catalog

# 扫描引擎与扫描规则
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Scanning

# 隔离区（存储/服务/路径校验/崩溃恢复）
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Quarantine

# 执行器与特殊项（休眠闸门/DISM/回收站）
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Execution

# 清单导出与复核报告
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Reporting

# 静态安全检索（I-1/I-5/I-6 的源码级证据）
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Architecture

# 只跑内核工程
dotnet test Code/tests/SpaceMaid.Core.Tests/SpaceMaid.Core.Tests.csproj
```

测试纪律：

- 一切 IO 测试的根目录必须在 `%TEMP%\spacemaid-tests-<guid>` 下，并在 `finally` 中删除；**测试不得触碰真实系统目录**（`C:\Windows\*` 等）
- 跨卷路径用假 `IVolumeProbe`（把两个临时目录标成不同卷）验证，不依赖真机多分区
- 休眠闸门、DISM 参数、占用跳过都用假 `ICommandRunner` / 假 `IFileSystem` 验证，**不真的执行 `powercfg` 或 `dism`**
- 真机删除动作永远由用户按需求 3.9 的流程自己执行，自动化只到 dry-run 与复核

## 当前工程状态（写文档时的真实情况）

- 已完成：`SpaceMaid.Core` 全部内核能力（安全底座、目录闭集、扫描、隔离区、执行器、报告、特殊项、设置、日志、组合根 `CoreServices`），提交历史见 `git log -- SpaceMaid`
- 进行中：`SpaceMaid.App` 界面层。`Views/MainWindow.xaml` 目前仍是骨架（注释写着"Task 13 会替换为三段式正式界面"），`App.xaml.cs` 尚未接入 `CoreServices`
- 尚未创建：`Code/scripts/gen-icon.ps1`、`Code/src/SpaceMaid.App/Assets/app.ico`、`Code/Directory.Build.props`、`Code/tests/SpaceMaid.Core.Tests/Reporting/CliContractTests.cs`、`Execution/DismComponentCleanupTests.cs`（测试方向：`DismComponentCleanupTests.Should_not_use_reset_base` 在实施计划里被点名，但**该测试文件当前不存在**，DISM 参数不含 `/ResetBase` 目前由 `HibernateGateTests` 的同名断言与静态检索测试覆盖）
- **只读 CLI（`--dry-run` / `--report`）尚未实现**：没有 CLI 工程，`App.xaml.cs` 也不解析参数。需求 9.1 提到 CLI，但它属于独立的界面层任务
- `SpaceMaid.App` 与 `SpaceMaid.App.Tests` 正被另一路改动；**改这两个目录前先看 `git status`**

## 构建与发布

```bash
dotnet build Code/space-maid.slnx
dotnet test Code/space-maid.slnx
dotnet publish Code/src/SpaceMaid.App -c Release
```

Release 发布参数已在 `Code/src/SpaceMaid.App/SpaceMaid.App.csproj` 中固化（仅 Release 生效）：`RuntimeIdentifier=win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`DebugType=none`、`SatelliteResourceLanguages=zh-Hans;en`。产物为 `bin/Release/net8.0-windows/win-x64/publish/SpaceMaid.exe`。

`app.manifest` 固定 `requireAdministrator`，并声明 Win10/11 兼容、per-monitor v2 DPI、`longPathAware`。程序集名为 `SpaceMaid`（工程目录名是 `SpaceMaid.App`）。详细发布流程见 `Docs/发布部署指南.md`。

## 文档

- `Docs/需求.md`（SRS 基线 v0.7，已冻结）
- `Docs/设计文档.md`（架构、安全不变量 I-1 至 I-6、模块设计、追溯矩阵）
- `Docs/实施计划.md`（TDD 任务级计划 Task 1-15 与检查点 A-E）
- `Docs/测试报告.md`（需求第 7 章 15 条验收标准逐条对照）
- `Docs/发布部署指南.md`（发布打包、UAC、杀软误报、升级卸载）
- `Docs/评审记录.md`（需求评审与代码评审台账）
