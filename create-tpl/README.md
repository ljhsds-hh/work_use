# create-tpl 项目模板快速生成工具

按固定的标准化模板一键生成工程骨架的 Windows 桌面小工具：输入**工程根目录**与**工程名称**（一行一个，支持批量），自动创建统一的项目初始目录结构与配套文档，免去手工建目录、建文件。

## 运行

运行 `Code\publish\CreateTpl.exe`（自包含单文件，无需安装 .NET 运行时）。首次启动需解压，约 10–20 秒，之后启动很快。

## 功能

### 参数输入

- **工程根目录**：手工输入或点「定位目录」选择；路径不存在时自动递归创建
- **批量架构清单**：一行一个工程名，自动忽略空行、去重（大小写不敏感），逐行校验非法字符 / Windows 保留设备名（CON、NUL 等）/ 尾点尾空格 / 超长等，校验不通过整体提示且不开始生成

### 生成的标准结构

```
工程根目录/工程名
├── CLAUDE.md     （预置固定说明文案：工程 AI 关键记忆文件）
├── README.md     （完全空白，0 字节）
├── Code/         （空文件夹，存放项目源码）
└── Docs/
    └── 需求.md    （完全空白，0 字节）
```

### 容错与反馈

- **防重复**：目标工程已存在 → 跳过，绝不覆盖或修改既有文件
- **单点失败隔离**：批量执行中任一工程失败 / 跳过，不影响后续工程继续创建
- **未完成清单**：以醒目红框列出失败与跳过的工程及原因，便于补建
- **失败回滚**：单个工程创建中途异常时尽力清理残缺目录，不污染根目录
- **流水线日志**：逐条展示每个工程的结果状态（成功 / 已存在 / 失败）与完整路径或失败原因
- **骨架拓扑预览**：按文件夹层级**可折叠**的树形预览，工程根节点带 `Initialized` 标记
- **全局容错**：非法输入、权限不足、路径异常均给出中文提示，程序不闪退（异常落盘 `%TEMP%\create-tpl-error.log`）

### 界面

Blueprint Studio 三栏工作台（蓝图源定义 / 流水线日志 / 骨架拓扑预览），支持**昼夜双主题**：页头按钮即时切换，或使用 `CreateTpl.exe --light` 以亮色启动。

## 技术栈

- C# / .NET 8 / WPF
- UI：HandyControl 组件库 + 自绘玻璃拟态模板 + 主题令牌（`Themes/Tokens.Dark|Light.xaml`，运行时切换）
- 架构：标准 MVVM 分层（View / ViewModel / Service），Service 层零 UI 依赖，可完整单元测试
- 发布：单文件自包含 EXE（win-x64，开启压缩，约 64 MB）

## 项目结构

```
Code/
  src/CreateTpl/           主程序
    Views/                 MainWindow（三栏工作台）
    ViewModels/            流程编排、命令、结果汇总
    Services/              模板创建引擎、输入解析、目录选择、消息通知
    Models/                结果与拓扑节点模型
    Helpers/               命令、转换器、主题服务、保留名校验
    Themes/               昼夜主题令牌字典
    Assets/                应用图标
  tests/CreateTpl.Tests/   xUnit 单元测试（40 用例）
Docs/
  需求.md                   需求规格说明书（唯一需求基线）
  设计文档.md               架构与模块设计
  测试报告.md               测试范围、结果与缺陷记录
  评审记录.md               代码评审与需求符合性核对
  发布部署指南.md           构建与单文件打包步骤
  UI预览.png / UI预览-亮色.png  界面预览（暗色 / 亮色）
  UI自动化验收脚本.ps1      基于 UI Automation 的端到端验收脚本
```

## 构建与测试

```bash
dotnet build Code/create-tpl.slnx
dotnet test Code/create-tpl.slnx
dotnet publish Code/src/CreateTpl -c Release    # 输出单文件 EXE 到 publish 目录
```

## 测试

40 个 xUnit 单元测试全部通过：

- `InputParserTests`（17）：分行解析、去重、非法字符 / 保留名 / 特殊名 / 超长 / 批量上限、根目录路径校验
- `ProjectTemplateServiceTests`（23）：固定结构生成、空白文件与预置文案、根目录自动创建、防重复不覆盖、失败隔离与回滚、进度上报、目录树预览

## 交付物

- 完整源码（`Code/`）
- 可运行的单文件 exe（`Code\publish\CreateTpl.exe`，通过 GitHub Release 发布）
