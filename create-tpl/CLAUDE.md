此处为工程关键记忆文件，请记录项目核心需求、开发规范、关键逻辑等关键信息，供AI迭代开发、上下文复用使用

## 项目核心需求

create-tpl：.NET 8 WPF 桌面小工具，输入工程根目录 + 工程名称（支持一行一个的批量输入），一键生成标准化个人项目初始模板：

```
工程根目录/工程名
├── CLAUDE.md    （预置本文档顶部固定文案）
├── README.md    （0 字节空白）
├── Code/        （空文件夹）
└── Docs/需求.md  （0 字节空白）
```

核心规则见 [Docs/需求.md](Docs/需求.md)（唯一需求基线）；技术方案见 [Docs/设计文档.md](Docs/设计文档.md)。

## 开发规范（硬约束）

- .NET 8（`net8.0-windows` 固定，不许换版本）+ HandyControl 3.5.1（UI 优先用 HC，无对应组件才用原生）
- 标准 MVVM：View 无业务逻辑；Service 不依赖 UI 类型（保证可单测）；ViewModel 通过接口依赖服务（IFolderPickerService / IMessageService）
- 发布形态：单文件自包含 EXE（参数已固化在 csproj 的 Release 配置，`dotnet publish src/CreateTpl -c Release` 一键打包）
- 全部用户可见文案为中文；关键步骤有中文注释

## 关键逻辑备忘

- **批量失败隔离**：批量按顺序逐个创建，任一工程失败/已存在只记录结果不中断后续；结束后汇总"未完成工程清单及原因"醒目提醒
- **防重复**：工程文件夹已存在 → 跳过并提示"当前工程已存在，无需重复创建"，绝不覆盖
- **失败回滚**：单工程创建中途异常会尽力删除残缺目录（不误删同名占位文件）
- **输入校验**：InputParser（纯静态可单测）——分行 Trim、忽略空行、大小写不敏感去重、非法字符/Windows 保留名/尾点/超长校验；根目录校验需逐段查非法字符（.NET Core 的 GetFullPath 已不做此检查）
- **HandyControl 坑**：hc: URI 命名空间解析附加属性（InfoElement.Placeholder / Growl.GrowlParent）会报 MC3072，须用显式 `clr-namespace:HandyControl.Controls` 前缀；Growl 消息容器用 XAML 的 `hcc:Growl.GrowlParent="True"` 注册；暗色主题下 `InfoElement.Placeholder` 占位符不渲染，输入框占位需自绘浮层（见 MainWindow.xaml 的 RootDirBox/NamesBox）

## UI 设计系统

- **UI 视觉基准：`模板.html`（用户提供的 Blueprint Studio 设计稿），最终界面与其一致**；预览截图：[Docs/UI预览.png](Docs/UI预览.png)（暗）/ [Docs/UI预览-亮色.png](Docs/UI预览-亮色.png)（亮）
- 三栏工作台布局：01 蓝图源定义（420px）/ 02 流水线日志明细 / 03 骨架拓扑预览；极光氛围背景 + 玻璃拟态面板 + 行号代码编辑器 + 渐变流光主按钮
- **昼夜双主题**：全部颜色为 `Themes/Tokens.Dark.xaml` / `Tokens.Light.xaml` 中的令牌，界面一律 `DynamicResource` 引用；`ThemeService.Apply(bool)` 运行时整体替换合并字典（含 HC 皮肤）；支持 `--light` 启动参数；改色只改 Tokens 文件
- HandyControl 保留职责：Growl 通知、皮肤字典；自定义视觉（玻璃按钮/胶囊/编辑器）为自绘 ControlTemplate（模板.html 美术级设计超出 HC 组件粒度，属合理取舍）

## 关键逻辑备忘（补充）

- **拓扑预览（可折叠树）**：BuildSummary 把成功工程构造为 `TreeNodeItem` 层级（工程根 → CLAUDE.md/README.md/Code//Docs/ → Docs/需求.md），右栏 `TreeView` 展示；工程根默认展开，其余层级点箭头（18×18 ToggleButton）展开/收起；未完成清单仍在流水线中栏底部红框提醒
- **行号规则**：`LineNumbersText` 空输入时仅 `01`，随输入行数递增；编辑器 `Loaded` 后查找内部 ScrollViewer 挂接 ScrollChanged → 转发行号栏偏移
- **应用图标**：`Assets/app.ico`（7 档尺寸程序化生成）；csproj `ApplicationIcon` + `Resource`；窗口 `Icon` 必须用**绝对 pack URI**（`pack://application:,,,/CreateTpl;component/Assets/app.ico`）——相对路径会按 Views/ 解析导致启动异常
- **WPF 自绘 TreeViewItem 坑**：自定义 `ControlTemplate` 里的 `ContentPresenter` 必须绑定 `ContentTemplate="{TemplateBinding HeaderTemplate}"`，否则节点显示类型名；纯描边 Path 的命中区只有线宽，需 `Fill="Transparent"` 或包 ToggleButton 才能可靠点击；`TreeNodeItem` 已重写 `ToString()` 供屏幕阅读器读取

## 验收与自动化

- **UI 自动化验收脚本**：[Docs/UI自动化验收脚本.ps1](Docs/UI自动化验收脚本.ps1)（UIA 定位 + 鼠标/键入驱动，可验证构建、拓扑渲染、折叠交互；`--light` 参数可验亮色）
- **环境限制**：中文输入法下脚本注入的 Enter / Ctrl+V / WM_PASTE 无法写入 WPF 文本框（应用侧无问题，已用 UIA 焦点与剪贴板断言排除）；多行输入请人工粘贴验证
- 全局异常兜底会把异常写入 `%TEMP%\create-tpl-error.log`，排查启动/运行问题先看该文件
- 单文件 EXE 首启需解压（约 10–20 秒），自动化脚本等待窗口须 ≥ 60 秒
- 之前版本（暗色简化版）的设计记录在 design-system/create-tpl/MASTER.md，已被模板.html 取代

## 验证与文档

- 单元测试：Code/tests/CreateTpl.Tests（40 用例，`DOTNET_ROLL_FORWARD=Major dotnet test` 可在本机 9/10 SDK 环境跑 net8 目标）
- 测试报告：[Docs/测试报告.md](Docs/测试报告.md)；代码评审：[Docs/评审记录.md](Docs/评审记录.md)；打包步骤：[Docs/发布部署指南.md](Docs/发布部署指南.md)
