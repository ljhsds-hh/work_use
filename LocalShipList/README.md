# Local 出荷 DLL 清单工具

按提交履历计算 local 出荷涉及的 DLL:在提交列表里选一条或**连续的多条**提交,
自动算出这些提交改动文件所属的全部工程,输出对应的 DLL/.xap 名称(每行一个)。

## 使用

1. 双击 `LocalShipList.exe`;
2. 选择仓库目录(或直接把 exe 放进仓库目录,会自动识别);
3. 选分支 → 点选一条提交,或按住 **Shift/Ctrl 选择连续的多条提交**;
4. 右侧输出 DLL 清单(每行一个),已自动复制到剪贴板。

选中的多条提交必须是**父提交链条上连续的**(中间不能跳过其它提交),
不连续会明确报错。多条连续提交按"净变更"计算:改动又改回的文件不算。

## 判定规则

- 改动范围 = 所选连续提交**每一笔碰过的文件**(逐条提交取并集;改了又改回也算);
- 改动文件 → 按 csproj 的 Compile/Page/Content 等条目归属工程;
- 每个受影响工程输出 AssemblyName + .dll(OutputType 为 Library 时);
- **Silverlight application 工程**(带 `SilverlightApplication=true` 或 `XapFilename`
  的工程)被改动时,同时输出它自身的 dll **和** 对应的 **.xap**;
- **xap 内含受影响 dll 时,xap 连带输出**:沿 ProjectReference/Reference 引用链,
  凡是 xap 的 application 工程包含(直接或间接)某个受影响工程,该 xap 也会列出
  (右侧"受影响工程"面板中标注"xap 内含受影响 dll");
- 不属于任何工程的改动(.sln、.config 等)忽略,但会在底部状态栏提示个数。

## 命令行模式

```bash
LocalShipList.exe <仓库路径> <最新SHA> [<更旧SHA> ...]
```

SHA 按新到旧排列,必须是连续提交。输出同样每行一个 DLL(适合脚本调用)。

依赖:电脑装有 git(在 PATH 中)。双击运行无需管理员权限。

## 构建

无需安装 Visual Studio,双击 `Code/build.cmd` 即可(调用系统自带 .NET Framework 编译器),
产物输出到 `Code/publish/LocalShipList.exe`。需求细节见 [Docs/需求.md](Docs/需求.md)。
