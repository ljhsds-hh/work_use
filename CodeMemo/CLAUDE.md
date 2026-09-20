# CodeMemo 工程关键记忆

## 项目定位

本地命令速查库（work_use 工具集之一）：分类收藏 PowerShell / Git 命令，支持备注、搜索高亮、一键复制、占位符参数填充、常用 Top10 快捷复制、暗色主题与字号、托盘常驻 + 全局热键。**明确不做命令执行**（用户拍板：命令多带参数，聚焦查阅→复制路径）。

## 关键需求决策（用户确认，勿擅自更改）

- 技术栈强制 .NET 8 + HandyControl 3.5.1；HC 没有的组件才允许 WPF 原生控件
- v1 仅 PowerShell / Git 两大分类 + 两级子分组（定义在 `Models/Catalog.cs`）
- 预置命令库 89 条（PS 38 + Git 51）由 AI 起草，用户后续在 UI 上自行修订
- 占位符统一成对英文尖括号 `<xxx>`；测试强制校验成对，详情页据此生成参数输入框
- 快捷键：Ctrl+F 搜索、Ctrl+N 新增、Enter/双击复制、Delete 删除、Esc 清空搜索
- 全局热键默认 `Ctrl+Alt+C`；左栏是「预设胶囊 + 录制」，被占用的组合会回退到原热键；关闭窗口默认收进托盘常驻（都可在左栏关掉）
- 主题支持「明亮 / 暗色 / 跟随系统」三选一
- 三栏布局：分类树 + 外观/常驻偏好 / 快捷面板 + 列表 / 详情

## 关键实现事实（易踩坑）

### 存储与数据

- 种子数据 `Assets/seed/commands.json` 为 EmbeddedResource，**仅首次启动导入** `%AppData%\CodeMemo\commands.json`，之后以用户文件为准（升级不覆盖用户数据）
- `LibraryStore.JsonOptions` 是唯一序列化入口，含 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`：默认编码器会把中文写成 `\uXXXX`、`<` 写成 `\u003C`（旧用户数据文件就是这样，不可读也不可手改）
- `SchemaVersion` 默认 0 = 文件未标注版本（手写文件）；Save 时统一写成 `LibraryStore.CurrentSchemaVersion`；加载时高于程序版本只提示不擅自改写
- 导入备份是 `commands.yyyyMMdd-HHmmss.bak`，按名称倒序保留最近 5 份——`File.Copy` 会保留源文件时间，所以**不能**按 LastWriteTime 排序
- 导入走 `Services/LibraryValidator`：必填字段不全的条目跳过并报告，Id 缺失/重复自动重新分配；自定义子分组不算不合规
- 用户偏好单独存 `settings.json`（`Services/SettingsStore` + `Models/AppSettings`），读写都做归一化，文件坏了回默认值不打断启动
- **写盘节流**（`Services/DeferredSaver` + `ISaveScheduler`）：复制只 `RequestSave()`，静默 1.5 秒后整库写一次（`DispatcherSaveScheduler` 用 DispatcherTimer 在 UI 线程回调）；新增/编辑/删除、导入、窗口 `OnClosed` 走 `SaveLibraryNow()` / `FlushPendingSaves()` 立刻落盘。**导入备份前必须先落盘**（备份的是磁盘文件，不然备份落后于内存里的统计）
- **导入方式**：`IDialogService.ChooseImportMode` 问合并 / 替换 / 取消（默认按钮是合并，回车走安全路径）；合并走纯函数 `Services/LibraryMerger` —— 内容指纹（分类/分组/标题忽略大小写与首尾空白，命令忽略首尾空白与换行、区分大小写）去重，同 Id 只覆盖描述字段并**保留本机 UseCount/LastUsedAt/CreatedAt**

### 分类树 / 搜索 / 列表

- 分类树节点是 `Models/FilterNode`，构建走纯函数 `Services/CommandTreeBuilder.BuildRoot`：预置分组全部保留（空分组也有节点，点进去看空态），自定义分组按名称追加，空分组/空分类落到「（未分组）」「（未分类）」兜底节点，**任一节点 Count 恒等于子节点 Count 之和**（有单测守住）
- **展开态**：FilterNode.IsExpanded 默认 true（根与大分类），子分组默认 false；重建前 `CaptureExpansionState()` 读旧节点的值（TwoWay 绑定已把用户操作写回），再套用到新节点 —— 这样不用 INPC 也能保住用户折叠状态
- `CommandSearch.Filter`：category/group 为 **null 才不过滤**，空字符串表示「筛未分类 / 未分组」条目（树的兜底节点靠这个）；有搜索词时忽略分类限定跨库搜
- 刷新（搜索/排序/增删改）会 Clear 重建 `VisibleCommands`，ListBox 绑定会把选中置空 —— 所以 `Refresh()` 先存下选中 Id，重建后按 Id 还原，否则详情页会在每次敲键盘时闪空
- 关键词高亮：纯函数 `Services/HighlightText.Split`（相邻/重叠命中合并）+ 自定义控件 `Views/HighlightTextBlock`。它只用 Inlines 渲染，**UIA/读屏读不到 Text**，所以列表 ItemContainerStyle 里补了 `AutomationProperties.Name="{Binding Title}"`
- 快捷面板榜单走 `Services/CommandRanking`（只收用过的条目）；复制会改变排序，所以 `CopyEntryText` 里顺带 `RefreshQuickPicks()`

### 外观（HandyControl 皮肤）—— 最坑的一处

- HC 3.5.1 **没有** `ThemeManager`/`ApplicationTheme`；皮肤 = 「颜色字典（`Themes/SkinDefault|SkinDark.xaml`）+ 样式字典（`Themes/Theme.xaml`）」一对资源（`SkinType` 枚举只有 Default / Dark / Violet）
- **样式字典里的画刷是「首次使用时」按当时的颜色字典算出来并固定的**。因此：
  - 只改 `hc:Theme` 的 `Skin`、或只替换颜色字典 → 只换颜色 token，界面画刷还是上一套（换肤看着没反应）
  - 复用已算过的样式字典实例（`ResourceHelper.GetTheme()` / HC 的 `SharedResourceDictionary` 缓存）→ 同样不生效
  - **正确做法**：每次应用皮肤都 `MergedDictionaries.Clear()` 后按 pack URI 现场 `new ResourceDictionary{Source=Skin*.xaml}` + `new ResourceDictionary{Source=Theme.xaml}`（`WindowsAppearanceService` 就是这么做的，同进程反复切换实测有效）
- 字号不用 HC：在 App.xaml 声明 `CodeMemoFontSize*` 资源，界面一律 `{DynamicResource}` 引用，运行时代码改这几个 key 即整体缩放（字体大小是普通资源值，没有画刷那种缓存问题）
- App.xaml 里保留 `<hc:Theme Name="HandyTheme" Skin="Default"/>` 只作设计期默认，启动时会被 `WindowsAppearanceService.Apply` 整体替换
- **跟随系统**：`ISystemThemeSource`（生产实现 `WindowsSystemThemeSource` 读注册表 `AppsUseLightTheme` + 订阅 `SystemEvents.UserPreferenceChanged`；测试喂假实现）→ `Apply("System")` 解析成真实皮肤并 Start 监听，系统亮暗变化回调里切回 UI 线程重切；切到固定主题会 Stop。`UserPreferenceChanged` 不是 UI 线程回调，必须 `Dispatcher.Invoke`

### 托盘常驻 / 全局热键

- 托盘用 HandyControl 自带的 `hc:NotifyIcon`（FrameworkElement，`Init()` 有 `_added` 幂等保护，在窗口 Loaded 里调用即可；`Icon` 用 pack URI 取 `Assets/app.ico`，取不到时 HC 会退回默认图标）
- 关闭收托盘：`OnClosing` 里 `_viewModel.MinimizeToTrayEnabled` 为真则 `e.Cancel = true; Hide();`，真正退出走托盘菜单（置 `_reallyExit`）
- 全局热键用 `HwndSource` 自建隐藏消息窗口 + `RegisterHotKey`（`Services/WindowsPlatformServices.cs` 的 `GlobalHotkeyService`），手势解析在纯函数 `Services/HotkeyGesture`（支持 A-Z、0-9、F1-F24、Space）；键盘 → 手势文本的转换在 `Services/HotkeyRecorder`（只有它碰 WPF 的 Key 枚举，Alt 组合键要先从 `Key.System` 取回 `SystemKey`）
- 热键界面用**预设胶囊（Button + CommandParameter）+ 录制按钮**，不要用 ComboBox：实测那个 `SelectedItem` 双向绑定在真机上鼠标点选后不会回写 ViewModel（键盘 Up/Down 对那个框也无效），换胶囊后 UIA 一次点通
- 热键冲突策略：启动注册失败 → 关掉偏好；用户新选/录制被占用 → 回退到原热键（`TrySetHotkey` 的 revert 分支）并提示，避免用户既没新热键又丢旧热键
- 窗口的 `OnPreviewKeyDown` 负责录制：只按修饰键就继续等，Esc 取消，其他按键吃掉并转成手势文本；录制态下 `OnKeyDown` 不响应任何快捷键

### 架构

- `MainViewModel` 不碰剪贴板 / 对话框 / 文件对话框 / 外观 / 热键，统一依赖注入 `AppPlatform`（`IClipboardService` / `IDialogService` / `IShellService` / `IAppearanceService` / `IGlobalHotkey`，WPF 实现在 `Services/WindowsPlatformServices.cs`，组合根在 `App.OnStartup`），否则 ViewModel 层没法单测
- HandyControl 资源键是 `ListBoxItemBaseStyle` / `TreeViewItemBaseStyle`（不是 `ListBoxItemBase`）；HC 3.5.1 **没有** Int2VisibilityConverter；Growl 用法：面板设 `hc:Growl.Token` + `hc:Growl.GrowlParent`，代码 `Growl.Register(token, panel)` + `Growl.Info(msg, token)`
- 工程/测试/发布规范与 work_use 其他工具一致：slnx、xunit、Release 单文件 win-x64
- 图标生成脚本 `Code/scripts/gen-icon.ps1`（PowerShell 内联表达式不能直接写在 New-Object 参数里，需先赋值变量）

## 验证方式

`dotnet test Code/code-memo.slnx`（151 个用例：种子合法性 / 分类树与计数 / 占位符 / 搜索排序与高亮 / 榜单 / 热键手势与录制 / 写盘节流 / 合并去重 / 存储往返与转义备份版本 / 偏好归一化 / 外观皮肤与跟随系统 / 导入校验 / MainViewModel）。`WindowsAppearanceServiceTests` 在 STA 线程里真建 Application 验皮肤画刷与跟随系统，**整个测试进程只能有一个 Application**，所以用 `Lazy` 保证只跑一次。

UI 冒烟用 UIA（鼠标合成点击会被输入法组合态吞掉，输入中文环境 SendKeys 打英文也会进 IME；用 UIA ValuePattern/Select/SetFocus/Invoke 加 keybd_event/mouse_event 更可靠）。踩过的坑：

- 给 `powershell.exe`（5.1）跑 .ps1 必须是 **UTF-8 带 BOM**，否则中文被当 ANSI 解析、脚本直接语法错误（pwsh 默认写出的文件不带 BOM，需用 `[IO.File]::WriteAllText` 配 `UTF8Encoding($true)` 重写一遍）
- PS 5.1 的 `Add-Type` 只支持 **C# 5**：表达式体成员（`=> ...`）会编译失败，得写成 `{ return ...; }`；静态方法带参数调用要写 `[T]::M($a, $b)`，空格隔开会报 unexpected token
- 截窗口用 **`PrintWindow(hwnd, hdc, 2)`**（PW_RENDERFULLCONTENT）直接抓窗口自身；`CopyFromScreen` 抓的是屏幕上最上层内容，而 `SetForegroundWindow` 常被前台锁挡住，实测会抓到桌面而不是应用（亮度/取色全错）
- 要往应用里发键盘，先让应用真的在前台：用 **应用自己的全局热键**（或 mouse_event 点一下窗口）激活，别指望 `SetForegroundWindow`
- 实机验证套路：把 `%AppData%\CodeMemo\settings.json`（动了命令库就一起备份 commands.json）先备份 → 写入待测偏好 → 启动取窗口句柄（UIA 按 ProcessId 找顶级窗口）→ 用 PrintWindow 取平均亮度/取样点颜色（暗色 ~46、明亮 ~240）→ 用 UIA/热键改设置后重测 → 结束还原文件并确认无残留进程
- 节流写盘这样验：先写一个 UseCount 全 0 的小命令库 → 启动后用 UIA 触发第一行的「复制」按钮 → 立刻读文件应该还是 `0,0`，等 2.5 秒再读应该出现 `1`（说明 DispatcherTimer 真跑了）
- 有两处**没法实机自动化**，只能靠单测 + 编译检查兜着：导入时的 Win32 文件选择框（`OpenFileDialog`）、托盘右键菜单（通知区域图标不在 UIA 树里）；对应逻辑（`LibraryMerger`、`OpenDataDirectoryCommand`）都有单测
- 组合框在 UIA 里很难驱动：`SelectionPattern.GetSelection()` 对未展开的下拉常常取不到项、`ExpandCollapsePattern.Expand()` 也不一定真展开；**能点按钮就别点下拉**（预设胶囊、托盘菜单这类 Button/MenuItem 用 InvokePattern 一点就通）。真要开下拉就用 `mouse_event` 点框体，展开后整个桌面按名字找项（`AutomationElement.RootElement`）再点
- 判断某个热键是否被系统/别的程序占用：`RegisterHotKey(IntPtr.Zero, id, mods, vk)` 返回 false 且 `Marshal.GetLastWin32Error() == 1409` 就是已被注册（本机 `Ctrl+Alt+Space`、`Ctrl+Shift+C` 就是这种）
- UIA 里 `ListBox` 只暴露已实例化的行（列表开了虚拟化），中文检索出的行可用 `AutomationProperties.Name` 断言
- Growl 轻提示的内容不一定能从 UIA 的 Text 元素里搜到，别把它当唯一断言点，改状态（settings.json / 窗口可见性 / 生效的热键）更可靠
