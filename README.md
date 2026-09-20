# 个人工作小工具集

日常工作中开发的小工具集合。每个工具独立成目录，包含源码、使用说明与构建配置。

## 工具索引

| 工具 | 简介 | 技术栈 | 文档 |
| --- | --- | --- | --- |
| [DLL 版本替换备份工具](DllTool/README.md) | 对比新版 DLL 与目标目录存量 DLL，完成旧文件安全备份与可控版本覆盖，全程留痕可回溯 | C# / WPF / .NET 9 | [需求说明](DllTool/Docs/需求.md) |
| [Local 出荷 DLL 清单工具](LocalShipList/README.md) | 按提交履历自动计算 local 出荷（紧急出盒）涉及的全部 DLL / XAP 名称，支持一条或连续多条提交，每行一个输出 | C# / WinForms / .NET Framework 4.x | [需求说明](LocalShipList/Docs/需求.md) |
| [create-tpl 项目模板快速生成工具](create-tpl/README.md) | 输入工程根目录与工程名称（一行一个，支持批量），一键生成标准化工程骨架与配套文档，单点失败隔离并汇总未完成清单 | C# / WPF / .NET 8 | [需求说明](create-tpl/Docs/需求.md) |
| [QuietRemind 任务提醒工具](QuietRemind/README.md) | 托盘常驻任务提醒：到点全屏遮罩+居中卡片提醒（背景可自定义），关机前拦截、开机补提醒，计划任务守护，提醒必达 | C# / WPF / .NET 8 | [需求说明](QuietRemind/Docs/需求.md) |
| [CodeMemo 命令速查库](CodeMemo/README.md) | 分类收藏 PowerShell / Git 常用命令（内置 89 条含避坑备注），全局搜索、参数占位符标注、一键复制并记录使用次数 | C# / WPF / .NET 8 | [需求说明](CodeMemo/Docs/需求.md) |

## 目录结构

```
work_use/
├── README.md            # 仓库索引
├── LICENSE              # 开源许可证
├── .gitignore
└── <工具名>/            # 每个工具独立目录
    ├── README.md        # 工具使用说明
    ├── Code/            # 源码与构建工程
    ├── Docs/            # 需求 / 设计文档
    └── publish/         # 构建产物（不入库，通过 Release 发布）
```

## 许可证

[The MIT License](LICENSE)
