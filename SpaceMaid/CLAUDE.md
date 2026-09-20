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
- 应用图标由 `Code/scripts/gen-icon.ps1` 程序化生成（**已创建**：产出 9 种尺寸的 `Code/src/SpaceMaid.App/Assets/app.ico`，381,038 字节，改设计只需改脚本参数重新生成，**勿手工编辑 app.ico**）

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
| `Core_should_not_read_bypass_style_environment_variables` | 内核不读名字含 `disable` / `skip` / `bypass` / `force` / `ignore` / `unsafe` / `nocheck` 的环境变量（I-6 的第二个入口；读 `SystemDrive` 这类系统变量不受影响） |

**界面侧同样有 5 条**（`ReadAppSources()` / `ReadAppXaml()`，都在同一个 `StaticSafetyTests` 里）：`App_should_not_contain_delete_calls`（界面**零容忍**，一个删除调用都不许有）、`App_should_not_offer_bypass_switches_or_reset_base`、`App_should_not_own_a_command_runner`（界面不得持有执行命令的能力）、`App_xaml_should_not_hardcode_colors`（界面 XAML 不得出现 `#RRGGBB` / `Color=` / `SolidColorBrush`——颜色只能取 HandyControl 皮肤资源或设计令牌）、`App_source_root_should_be_discovered`。整组当前共 **17** 条。

**这条规则已于 2026-09-20 收紧（改动前必读）**：`Core_should_not_use_component_store_reset_base` 现在断言的是**裸字串** `ResetBase`（不区分大小写）在整个 Core 源码中都不出现——原来的带斜杠写法"看着通过"，却拦不住有人把开关当成参数拼进命令。为满足收紧后的规则，`l3.component-store` 的文案已改为「本工具不提供「重置基线」选项」，不再出现英文开关名。

如果以后确实需要在文案里重新写出这个开关名，**必须同时放宽这条断言**，并在评审记录里说明理由；但要先想清楚代价：写出开关名只对排障有用，而漏掉一次命令参数会让用户再也无法卸载已安装的更新。

> 注意：全局约束里的"**不得实现 `/ResetBase`**"指的是**实现**（命令参数），`DismComponentCleanup.Arguments` 常量里确实没有它，参数也不接受外部拼装。

## 界面设计系统（2026-09-20 重做，改界面前必读）

界面从"控件堆叠"重做成一套完整设计系统。三条硬规则：

1. **颜色只来自 HandyControl 皮肤**（`App.xaml` 合并 `SkinDefault.xaml` + `Theme.xaml`），
   界面里一律写 `{DynamicResource PrimaryBrush / RegionBrush / SecondaryRegionBrush / BorderBrush /
   PrimaryTextBrush / SecondaryTextBrush / ThirdlyTextBrush / DangerBrush / LightDangerBrush / DarkDangerBrush …}`。
   **不写任何色值**：`StaticSafetyTests.App_xaml_should_not_hardcode_colors` 会拦 `#RRGGBB` / 字面量 `Color=` /
   `SolidColorBrush`（注释不算、`Color="{DynamicResource X}"` 不算——这条断言已按这两点收紧，别改回去）。
2. **尺度只来自 `Themes/DesignTokens.xaml`**：字号 `SmFontSize*`（30/22/16/13/12/11）、间距 `SmSpace*`(4/8/12/16/20/24/32)、
   圆角 `SmRadius*`、文字样式 `SmTextDiskplay/Metric/Title/Section/Body/Caption/Meta/Label/Danger`、
   图标 `SmIcon*`（18 个 24×24 填充路径，`Path Stretch="Uniform"` 缩放）、按钮 `SmButtonPrimary/Secondary/Danger/Ghost/Link`、
   色标 `SmChip/ChipSafe/ChipAccent/ChipInfo/ChipCaution/ChipDanger`（**Border 目标类型**，不是 hc:Tag）、
   导航项 `SmNavItem`(RadioButton) / `SmNavAction`(Button，用于"设置"这种动作项)。
3. **交互控件优先 HandyControl**：`hc:TextBox` / `hc:NumericUpDown` / `hc:ToggleBlock` / `hc:TabControl` /
   `hc:CircleProgressBar` / `hc:Card` / `hc:Divider` / `hc:ScrollViewer` / `hc:Empty` / `hc:LoadingLine` / `hc:Growl`。
   **HC 3.5.1 没有** Button / CheckBox / Switch / Expander / ProgressBar → 这五类用 WPF 原生控件；
   其中**按钮外观由本设计系统自带 ControlTemplate**（见下面易踩坑第 6 条）。

主窗结构（`Views/MainWindow.xaml`）：左侧导航栏（212px，品牌 + 三个页面 + 设置动作 + C 盘可用空间）
+ 右侧内容区（页头 64px：页面标题/副标题 + 四个操作按钮 → 全局提示条 → 页面 → 状态栏 38px）。
三个页面：**磁盘概览**（占用环 + 本次可处理主数字 + 三格 KPI + 四个分级卡片 + 隔离区摘要 + 最近执行结果）、
**清理计划**（本分级工具条 + 扫描中/空态 + 四个分级分组 + 项行 + 执行结果）、**隔离区**（占用 + 批次列表 + 还原）。
设置窗口（`Views/SettingsWindow.xaml`）同套令牌：页头 + `hc:TabControl` 三页卡片 + 页脚"保存/关闭"。

### 界面验证（本机看不了图，只能这样验）

```powershell
# 启动应用（dotnet 宿主绕开 requireAdministrator 的 UAC）→ 点掉"权限不足"闸门 → 点「重新扫描」→
# 逐页导出 UIA 树 + PrintWindow 像素统计 → 12 条断言 → 截图落 D:\logs\SpaceMaid\ui\
powershell -NoProfile -ExecutionPolicy Bypass -File SpaceMaid\Code\scripts\probe-ui.ps1
```

断言覆盖：主窗标题、扫描能跑完（只读）、清理计划页元素数、隔离区页元素数、
**导航栏与页头表面亮度差 ≥ 8**（层级）、**横带亮度极差 ≥ 8**（上下有层次）、最高频色占比 ≤ 90%（不是一片死白）、
颜色数 ≥ 120、设置窗口能打开且有「保存设置」、截图字节数。
脚本**只点这几个按钮**：重新扫描 / 左侧导航 / 设置 / 关闭——绝不点清理、清空、还原。
脚本带 UTF-8 BOM（PS 5.1 才按 UTF-8 解析中文），别去掉。
## 验证方式

`dotnet test Code/space-maid.slnx`

**数字一律以当场 `dotnet test` 的输出为准，本文档不钉任何固定值。** 本项目用例数增长很快（曾经历 205 → 232 → 311 → 314 → 365 几个阶段），把它写死过一次已经过时一次，所以这里只留方法、不留快照。

> **注意工作区可能不等于 HEAD**：`SpaceMaid.App` / `SpaceMaid.App.Tests` / `SpaceMaid.Core` 常有并发改动。**跑测试之前先 `git status`**，并用 `git diff` 确认你读到的是哪一版代码；测试红了先分清是"自己改坏的"还是"别人正在改的"（并发开发中常见：新增了断言用例，而对应的生产实现还没写完）。

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

# 只读 CLI（参数解析与"绝不提供删除能力"）
dotnet test Code/space-maid.slnx --filter FullyQualifiedName~Cli

# 只跑内核工程
dotnet test Code/tests/SpaceMaid.Core.Tests/SpaceMaid.Core.Tests.csproj
```

> 界面不是只能"人工看"：`Code/scripts/probe-ui.ps1` 用 UIA 树 + 窗口像素统计给出 12 条可复跑断言（见上节）。

测试纪律：

- 一切 IO 测试的根目录必须在 `%TEMP%\spacemaid-tests-<guid>` 下，并在 `finally` 中删除；**测试不得触碰真实系统目录**（`C:\Windows\*` 等）
- 跨卷路径用假 `IVolumeProbe`（把两个临时目录标成不同卷）验证，不依赖真机多分区
- 休眠闸门、DISM 参数、占用跳过都用假 `ICommandRunner` / 假 `IFileSystem` 验证，**不真的执行 `powercfg` 或 `dism`**
- 真机删除动作永远由用户按需求 3.9 的流程自己执行，自动化只到 dry-run 与复核

## 当前工程状态（写文档时的真实情况）

- 内核 `SpaceMaid.Core`：**全部完成**——安全底座、目录闭集、扫描（流式枚举 + 单项界限）、隔离区（含断电自检与一键还原）、执行器、报告、特殊项、设置、日志、组合根 `CoreServices`、只读 CLI。提交历史见 `git log --oneline -- SpaceMaid`
- 界面 `SpaceMaid.App`：**收尾完成**。`App.xaml.cs` 已接入 `CoreServices`（启动编排：解析命令行 → 创建内核 → `Prepare()` 启动自检 → 组装平台服务 → 建 `MainViewModel` → 显示主窗），`ViewModels/`（`MainViewModel`、`CleanItemViewModel`、`SettingsViewModel`）与 `Views/`（`MainWindow`、`SettingsWindow`）已就位，`Themes/DesignTokens.xaml` 收敛设计令牌。界面冒烟证据：`Code/scripts/smoke-ui.ps1` PASS
- **只读 CLI 已接进 `SpaceMaid.exe`**（`0ad7e57`）：`App.xaml.cs` 解析 `e.Args`，`CliOptions.IsCommandLineInvocation` 为真就走 CLI 分支并 `Shutdown(exitCode)`，**绝不回落到 GUI**——否则一次手滑的 `--clean` 会变成"打开工具 + 顺手做启动维护"（顺带删掉到期的隔离批次）。用法错误（含 `--clean` 这类被拒开关）退出码 **2** 并提示"本工具不提供命令行清理能力"；成功 **0**；执行失败 **1**。设计决策 D-2 "CLI 内置于 App.exe" 至此真正落地，CLI 不再只能通过内核级 `CliRunner` 直接调用
- **已创建**（此前本文档记为"尚未创建"）：
  - `Code/scripts/gen-icon.ps1`：程序化生成应用图标，产出 9 种尺寸（16/20/24/32/40/48/64/128/256）的 `Code/src/SpaceMaid.App/Assets/app.ico`（381,038 字节，已逐像素验证）
  - `Code/src/SpaceMaid.App/Assets/app.ico`：与 `SpaceMaid.App.csproj` 的 `<ApplicationIcon>` 配套（**勿手工编辑**，改设计就改脚本参数重新生成）
  - `Code/Directory.Build.props`：解决方案级构建属性，**仅 Release** 生效 `DebugType=none` / `DebugSymbols=false`。原因是 `SpaceMaid.Core.pdb` 会被拷进 publish 目录，让"一个 exe 免安装"当场破功；加入后 publish 目录只剩 `SpaceMaid.exe`
  - `Code/scripts/smoke-ui.ps1`：界面冒烟脚本，用 dotnet 宿主加载 `SpaceMaid.dll`（**绕开 `requireAdministrator` 的 apphost，避免无人值守时卡在 UAC**），启动后 3 秒内枚举该进程顶层窗口按标题判定。实测 PASS：匹配到标题"权限不足"，退出码 0，无进程残留。它从不点击任何按钮，所以不可能触发清理动作
- **代码侧没有遗留待办**。实施计划点名的 `DismComponentCleanupTests.Should_not_use_reset_base` 其实**早就存在**：类定义在 `Code/tests/SpaceMaid.Core.Tests/Execution/HibernateGateTests.cs`（同一文件里两个类），对应用例是 `Should_use_fixed_official_command_only`——断言实际下发的命令等于常量、且 `DismComponentCleanup.Arguments` 不含重置基线开关。与实施计划的**唯一差异是文件名**（计划里写的是独立文件 `DismComponentCleanupTests.cs`），**不是"测试缺失"**；本文档与测试报告此前把它记成"尚未创建"，已更正（测试报告 G-6）
- **仍未在真机执行过真实清理**（需求 3.9：工具只出清单，用户审阅后自己执行，工具再复核）。所有实测都停在 dry-run 与复核这一侧——**任何地方都不要写成"已验证清理成功"**；需要"清理有效"的证据就必须先有一次真实人工清理
- `SpaceMaid.App`、`SpaceMaid.App.Tests` 与 `SpaceMaid.Core` 的部分文件正被另一路并发改动；**改这些目录前先看 `git status`**，不要假设工作区等于 HEAD

### 易踩坑（都是实际踩过的）

1. **扫描必须同时有流式枚举与单项界限**。`IFileSystem.EnumerateFilesStreaming` + `ScanEngine.MaxFilesPerItem = 20000` + `DefaultItemTimeBudget = 15 s` 三件事缺一不可：`%LOCALAPPDATA%\pnpm\store` 这类内容寻址缓存是十万级小文件，修复前光是 `l2.dev-caches` 这一项就要跑 20 s 以上，整机 dry-run 能拖到十分钟还出不了结果。命中界限**不是失败**——取已收集的部分照常处理，并在清单里如实写明"结果可能不完整"（对抗式评审 F-12）。提交 `2fd7422`
2. **"总容量"必须靠 `IVolumeCapacityProbe`**。界面的磁盘总览与清单头部的总容量都读它，而 `IVolumeProbe` 只回答"卷根 / 剩余空间 / 介质 / UNC"四件事，**不能只靠 `IVolumeProbe`**；探针缺省时那个数字会显示成 `0 B`（首次真机 dry-run 就出现过"系统盘：C:\（总容量 0 B，可用 64.54 GB）"）。`CoreServices.Create` 的缺省值是 `WindowsVolumeCapacityProbe`。提交 `2fd7422`
3. **隔离区路径校验的两个目录强度不一样**（`QuarantinePathValidator`）。真正落地的是 `<基路径>\SpaceMaid\Quarantine`，它才是**强校验**对象：既不能落在禁止目录树里，也不能命中凭据/还原点等禁止段。**基路径只拒绝落在禁止树内的**（网络路径、磁盘根、不可写一律仍然拒绝），**不能再对基路径用 `Denylist.IsDenied` 整体判定**——默认基路径就是 `%LOCALAPPDATA%`，而它是禁止清单里的"用户目录根本身"，一旦那样判，默认配置开箱即"隔离区不可用"，整条清理链路都跑不起来。提交 `2fd7422`
4. **信息项的体积绝不能算进"可处理"口径**（本机真机 dry-run 实测踩到）。`l3.pagefile` 的 `ActionKind` 是 `InformationalOnly`——它只展示体积、**永不执行**（`CleanExecutor` 直接跳过、界面连勾选框都没有、`Denylist` 还硬拦着 `pagefile.sys`）。早先 `MainViewModel.ProcessableBytes` 与 `CleanPlan.PlannedFileCount/PlannedBytes` 把它一起求和，于是同一台机器报出"39134 个文件，21.32 GB / 可处理 21.32 GB"，其中约 **15 GB 是 `C:\pagefile.sys`**，占"可处理"总量的七成——这等于向用户承诺一件不会发生的事。修正后的口径是：界面 `IsProcessable` 与清单 `CleanPlan.Actionable` **必须用同一判据**（排除 `InformationalOnly`），清单额外单列一行"仅展示、不执行：页面文件 15 GB（不计入上面的可处理体积）"。修正后同机实测为 **39145 个文件 / 6.32 GB**。**改体积求和的地方先问一句"这一项到底会不会执行"**
5. **"清单行数"与"计划文件数"不是一回事**。清单 csv 里信息项也占一行，所以复核基准必须比 `ManifestFileCount`；拿 `PlannedFileCount` 去比会因为信息项永远差一行，**每次都误报"勾选被改动过"**

6. **设计令牌键不能重复，引用不能打错**。重复键会让 WPF 在**运行期**抛 `Item has already been added. Key in dictionary: 'X'`
   并让整个界面启动失败（本轮真的撞上过：几何 `SmIconWarning` 与样式 `SmIconWarning` 同名）。
   引用不存在的 `Sm*` 键则抛"找不到名为 X 的资源"；`DynamicResource` 更阴——它只是静默变 null（界面看起来"没上色"）。
   现在两条都由 `StaticSafetyTests.Design_tokens_should_be_unique_and_referenced_keys_should_exist` 守着。
7. **不要 `BasedOn` HandyControl 的按钮样式键**。HC 3.5.1 的键名不全可靠：`ButtonPrimary`/`ButtonDefault`/`ButtonDanger` 在，
   但 **`ButtonTransparent` 不存在**（`BasedOn` 它会让启动直接抛"找不到资源"）。按钮外观已改为本设计系统自带模板。
8. **启动顺序：主窗必须先登记为 `Application.MainWindow`，再跑 `viewModel.Initialize()`**。
   Initialize 里会弹"权限不足"对话框（需求 3.6-2），而 WPF 把**第一个显示出来的窗口**记为 `Application.MainWindow`；
   配合 `ShutdownMode.OnMainWindowClose`，那个对话框一被点掉就把整个应用关掉——真机现象是
   "未提权启动 → 点掉提示 → 程序直接消失"。`StaticSafetyTests.App_should_register_main_window_before_running_startup_checks` 守着这个顺序。
9. **对话框的 Owner 必须是"已显示"的窗口**，否则 WPF 抛"无法将 Owner 属性设置为之前未显示的 Window"。
   `DialogService.HostWindow()` 用 `IsLoaded` 判断，拿不到就退回无 Owner 的模态框。
10. **事件订阅容易漏**。`RequestOpenSettings` 曾经**没人订阅**：ViewModel 侧事件、命令、导航分支都齐了，
    结果「设置」按钮点下去毫无反应，设置面板整块进不去，而界面上看不出任何异常。
    `StaticSafetyTests.App_should_wire_settings_request_from_viewmodel` 守着 `+=` / `-=` 成对存在。
11. **界面相关的坑只有把界面真跑起来点一遍才会暴露**（启动失败、按钮没反应、颜色没上）。
    改完界面**必须**跑 `probe-ui.ps1`，它会先把"权限不足"闸门点掉再验证真实主窗。

## 构建与发布

```bash
dotnet build Code/space-maid.slnx
dotnet test Code/space-maid.slnx
dotnet publish Code/src/SpaceMaid.App -c Release
```

Release 发布参数已在 `Code/src/SpaceMaid.App/SpaceMaid.App.csproj` 中固化（仅 Release 生效）：`RuntimeIdentifier=win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`EnableCompressionInSingleFile=true`、`SatelliteResourceLanguages=zh-Hans;en`。

另有解决方案级 `Code/Directory.Build.props`（仅 Release）设 `DebugType=none` / `DebugSymbols=false`：`SpaceMaid.Core.pdb` 会跟着被拷进 publish 目录，让"一个 exe 免安装"当场破功。**Debug 必须保留 pdb**，所以这条不能提到无条件生效。

实测：`dotnet publish Code/src/SpaceMaid.App -c Release` 产出 `bin/Release/net8.0-windows/win-x64/publish/SpaceMaid.exe`，**67,217,692 字节（64.1 MB），且 publish 目录下只有这一个文件**。

> **发布时必查**：publish 之后 `Get-ChildItem` 一下 publish 目录，**必须只剩 `SpaceMaid.exe` 一个文件**。多出任何 `*.pdb` / `*.dll` / `*.json` 都说明有属性被改回或新增工程漏了 `Directory.Build.props`，对外承诺的"单文件免安装"就不成立了。

`app.manifest` 固定 `requireAdministrator`，并声明 Win10/11 兼容、per-monitor v2 DPI、`longPathAware`。程序集名为 `SpaceMaid`（工程目录名是 `SpaceMaid.App`）。详细发布流程见 `Docs/发布部署指南.md`。

## 文档

- `Docs/需求.md`（SRS 基线 v0.7，已冻结）
- `Docs/设计文档.md`（架构、安全不变量 I-1 至 I-6、模块设计、追溯矩阵）
- `Docs/实施计划.md`（TDD 任务级计划 Task 1-15 与检查点 A-E）
- `Docs/测试报告.md`（需求第 7 章 15 条验收标准逐条对照）
- `Docs/发布部署指南.md`（发布打包、UAC、杀软误报、升级卸载）
- `Docs/评审记录.md`（需求评审与代码评审台账）
