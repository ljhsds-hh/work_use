# `.portmarks` 格式规范 v1

本工具专用的收藏夹交换格式，界面上的「导出源收藏夹 / 从文件导入」就用它。
目标只有一个：**结构无损、可以往返、可以演进**。

## 顶层结构

```json
{
  "format": "portmarks",
  "version": 1,
  "createdAt": "2026-11-26T12:00:00.000Z",
  "source": { "browser": "edge", "ua": "Mozilla/5.0 ..." },
  "tree": [ /* 顶层容器，固定 2 个 */ ]
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `format` | string | 固定 `"portmarks"`，用于识别文件身份 |
| `version` | number | 当前为 `1`，用于向后兼容 |
| `createdAt` | string | ISO 8601 时间，仅作参考，不参与导入逻辑 |
| `source` | object | 来源信息：`browser` 为 `edge` / `chrome` / `chromium`，`ua` 为原始 UA |
| `tree` | array | 顶层容器数组，索引 0 = 收藏夹栏，1 = 其他收藏夹 |

`source` 只是溯源用，导入时**不**作为判断依据。任何浏览器导出的包都能导入任何浏览器。

## 顶层容器

```json
{ "slot": "bar", "title": "收藏夹栏", "children": [ /* 节点 */ ] }
```

`slot` 取值为 `bar`（收藏夹栏）或 `other`（其他收藏夹）。导入时按 **索引** 映射到目标的对应容器，`slot` 仅作展示与校验辅助。

## 节点

只有两种，用 `type` 区分。

**文件夹**

```json
{ "type": "folder", "title": "技术", "children": [ /* 节点 */ ] }
```

`children` 可以为空数组（空文件夹会被原样创建）。层级不限。

**书签**

```json
{ "type": "url", "title": "MDN", "url": "https://developer.mozilla.org/" }
```

`url` 必须是非空字符串。协议不限，`javascript:` 书签（书签小工具）同样合法。

## 校验规则

导入时按顺序检查，任一条不满足即整体拒绝，不做部分导入：

1. 内容是合法 JSON 对象
2. `format === "portmarks"`
3. `version === 1`
4. `tree` 是数组
5. 每个节点有 `type`，且只能是 `folder` / `url`
6. `folder` 的 `children` 存在时为数组
7. `url` 节点的 `url` 非空字符串

## 不写入格式的内容

以下字段**故意不存**，避免制造"看起来能还原、其实还原不了"的假象：

- `id` —— 浏览器内部标识，跨浏览器无意义
- `dateAdded` —— 扩展 API 无法指定创建时间，存了也用不上
- `parentId` —— 由树形 `children` 自然表达
- 图标、访问次数、上次访问时间 —— 与"复制收藏夹"无关

## 演进策略

- 新增可选字段：不升 `version`，旧版本读到忽略即可
- 改变已有字段语义或结构：`version` +1，导入端遇到不认识的版本明确报错，不做猜测式兼容
