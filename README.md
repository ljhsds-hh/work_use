# 个人工作小工具集

日常工作中开发的小工具集合。每个工具独立成目录，包含源码、使用说明与构建配置。

## 工具索引

| 工具 | 简介 | 技术栈 | 文档 |
| --- | --- | --- | --- |
| [DLL 版本替换备份工具](DllTool/README.md) | 对比新版 DLL 与目标目录存量 DLL，完成旧文件安全备份与可控版本覆盖，全程留痕可回溯 | C# / WPF / .NET 9 | [需求说明](DllTool/Docs/需求.md) |

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
