# DLL 版本替换备份工具

对比新版 DLL 资源与目标目录存量 DLL，完成**旧文件安全备份 + 可控版本覆盖替换**的 Windows 桌面工具。提供两种数据源模式，操作全程留痕、可回溯、可核查。

## 运行

运行 `publish\DllTool.App.exe`（自包含单文件，无需安装 .NET 运行时）。

## 功能

### 模式A · 实体DLL文件夹（备份 + 可选覆盖）

1. 选择「新DLL文件夹」→「目标目录」→「备份存储根目录」
2. 点击「开始执行」：静默备份两端重合的旧版 DLL 到专属备份目录（**复制**，保留目录结构）
3. 弹出覆盖确认框：确定则用新版 DLL 覆盖目标目录同名文件；取消则仅备份
4. 匹配规则：**相对路径精确匹配**（含目录层级、区分大小写）；两端不重合的文件一律不参与

### 模式B · DLL清单文本文件（仅备份）

1. 选择清单文件（每行一个相对路径，如 `core.dll` 或 `plugins\foo.dll`）→「目标目录」→「备份存储根目录」
2. 点击「开始执行」：清单与目标目录匹配的文件被复制到专属备份目录；未匹配条目仅记日志

### 通用

- **备份目录规则**：根目录为空直接使用；非空自动创建不重复命名的子文件夹，历史备份互不干扰
- **备份清单**：每次操作后生成 `backup_list.txt`（相对路径一行一条，失败条目以 `#FAILED` 标记）
- **结果展示**：主界面表格逐行展示备份/覆盖明细（相对路径/操作类型/结果/失败原因），失败/跳过醒目区分
- **持久化日志**：全生命周期操作写入 `D:\logs\DllTool\`，按日期切分、保留 30 天
- **背景自定义**：标题栏右侧可设置背景图片 + 拖动「背景强度」滑块调节，设置自动保存
- **安全校验**：新DLL目录与目标目录相同/包含时拒绝执行；备份根目录与目标目录/新DLL目录重叠时拒绝执行

## 技术栈

- C# / .NET 9 / WPF
- UI：HandyControl 组件库 + 自定义主题
- 架构：MVVM（CommunityToolkit.Mvvm）+ 依赖注入
- 分层：`DllTool.Core`（领域逻辑）/ `DllTool.Infrastructure`（文件IO、日志）/ `DllTool.App`（WPF UI）

## 项目结构

```
Code/
  src/
    DllTool.Core/          领域模型、匹配逻辑、两模式执行器、校验器
    DllTool.Infrastructure/ 文件系统服务、持久化文件日志
    DllTool.App/            WPF 界面、MVVM、依赖注入、全局异常处理
  tools/
    SmokeTest/             核心逻辑冒烟测试
    UiTest/                UI 流程测试（覆盖模式独立状态）
Docs/
  需求.md                  需求规格说明书
```

## 测试

```bash
dotnet run --project Code/tools/SmokeTest   # 核心逻辑测试
dotnet run --project Code/tools/UiTest      # UI 流程测试
```

## 交付物

- 完整源码（`Code/`）
- 可运行的单文件 exe（`publish\DllTool.App.exe`，通过 GitHub Release 发布）
- 持久化日志位于 `D:\logs\DllTool\`；备份目录由用户在运行时指定
