# CodeFormatter 工程关键记忆

## 项目定位

work_use 工具集之一：**单文件离线代码格式化 / 压缩器**（Forge · 代码锻造）。九种语言（HTML / JS / CSS / XAML / XML / SQL / C# / Java / JSON），粘贴 → 自动识别 → 格式化或压缩 → 双栏对照复制走。

三条不可动摇的产品性质：

1. **单文件零依赖**：全部代码就在 `Code/CodeFormatter.html` 一个文件里，双击即用，不引入任何外部 JS / CSS / 字体文件，不做构建。加功能就改这个文件，别拆工程、别加打包步骤；
2. **完全离线、代码零出域**：不做任何网络请求、遥测、上报（用户自填背景图 URL 的图片加载是唯一例外）；代码内容也绝不进 `localStorage`（只存主题与背景配置）；
3. **只排版不改语义**：格式化与压缩都不做语法校验、不重构；凡是会改变语义的已知缺陷，必须如实写进 [Docs/需求.md](Docs/需求.md) 第 6 章的已知限制表，不许藏着。

## 架构（单文件三段）

| 段 | 内容 |
| --- | --- |
| CSS（`<style>`） | 全部设计令牌在 `:root` / `[data-theme="dark"]` / `[data-theme="light"]` 三组 CSS 变量里，自定义背景靠 `html[data-custom-bg]` 系列覆盖规则 + `--bg-glass` / `--bg-blur` 两个运行时变量 |
| HTML（`<body>`） | 顶栏（品牌 + 语言下拉 + 操作按钮 + 背景 / 主题）→ 状态栏 → 双栏编辑器（行号 gutter + 高亮层 + textarea） |
| JS（末尾 `<script>`） | `detectLanguage` 打分识别 → 九个引擎（`engines` 注册表）→ 编辑器组件（`createEditor`）→ 主题 / 背景 / 剪贴板 / 快捷键 |

## 关键实现事实（改代码前必读）

### 引擎分层

- **JSON** 直接 `JSON.parse` / `JSON.stringify`，非法输入抛「JSON 解析失败：+ 原文」。
- **XML 家族**（XML / XAML / HTML）共用 `parseXmlTokens` + `parseTag` + `formatXmlFamily` / `minifyXmlFamily`，只有缩进宽度（XML / HTML 2 空格、XAML 4 空格）和 HTML 特例不同：
  - `HTML_VOID`（14 种 void 元素）不加缩进深度、不自动补 `/`；
  - `HTML_RAW`（script / style / textarea / pre）内容**原样保留不重排**；
  - HTML 压缩时 `<!--[if ...]` 条件注释保留，其余注释丢弃。
- **CSS**：`formatCss` 先 `cssStrip` 剥掉全部注释再重排——注释丢弃是有意设计（K-4）；`url(...)` 内分号、`--var` 自定义属性有专门保护；minify 用「字符串字面量与代码分离」两段式压缩，字符串里的空白绝不压。
- **JS**：`scanJs` 做词法（正则字面量按前文消歧 `regexAllowed()`；模板串 `` `...${...}` `` 整体扫描），`formatJs` K&R + `} else {` 合行 + 运算符**配对合并**（`==` `===` `=>` `&&` 等多字符运算符不会被拆散）。**属性冒号 `{a: 1}` 贴身靠 `colonIsProperty` 向前扫**（先遇 `,`/`}` 且不在未闭合三元里→属性，先遇 `;`/`)`/裸 `:`→三元），`case 1:` 标签冒号同样贴身。`minifyJs` 有 ASI 保护（`keepNewline`：`return` / `throw` 行尾、`++` / `--` 前缀、`) (` 相邻等场景保留换行）。
- **SQL**：`tokenizeSql` 后按 `SQL_MAJOR` 主子句表换行（SELECT / FROM / WHERE / 各类 JOIN …），关键字大写只对 `SQL_KEYWORDS` 集合内的词生效（函数名不大写）；顶层逗号换行、括号内不换。
- **C# / Java**：共用 `formatBraceLang` + `minifyBraceLang`，C# 用专用 `csScan`（`@"..."` 逐字串 `""` 转义、`$"..."` 插值串花括号计数、`#` 预处理指令整行按 linec 保留），Java 用通用 `scanCStyle`。C# 源码含预处理指令时走 `formatWithDirectives`（先去行尾空白再整体排版）。
  - **复合运算符必须成对合并**（`==` `!=` `<=` `>=` `&&` `||` `+=` `*=` `<<=` `>>=` `??=` `=>` `->` `?.` `??` `::` 位移等）：`scanJs`/`csScan` 是逐字符出 token 的，运算符分支先做相邻 token 的最大吞并再排版，否则 `==` 会被排成 `= =`（历史缺陷 K-1，2026-09-22 已修，回归在冒烟脚本里）。合并后 `++` / `--` 按前后文区分后缀贴身 / 前缀留空格。
  - **泛型用 `isGenericStart` 向前扫配对 `>` 判定**（扫描到 `( ) ; { }` 或运算符即判负，`, . ?` 合法，单字符 word 也放行——Java 的 `scanCStyle` 是逐字符 token，字母也是 code；**前有无空格都行**，`public <T> T M()` 的类型参数表也成立），`generic` 计数器保证 `Dictionary<string, List<int>>` 嵌套闭合不加空格；扫描不到配对就按比较符加空格（历史缺陷 K-2，同日已修）。泛型**开括号不要 trim 行尾空白**，否则 `public <T>` 的空格会被吃掉。
  - **可空 `?` 用 `isNullableMark` 判定**：贴在类型后且向前扫先遇 `;` `{` `}` 而非 `:` 就贴身输出（`int? x` / `a?[0]`）；先遇 `:` 是三元。`?.` 在配对合并里先行处理。
  - 一元前缀集合是 `!` `~` 与无操作数在前的 `+` `-` `*` `&`（`&` 必须在列表里，否则 `var p = &x` 排成 `& x`）。
  - **Java 注解 `@Xxx(...)` 行尾要 flush 独立成行**（`\n` 分支的正则里），否则 `@Override` 会和下一行声明拼在一起。
  - 括号内的 `;`（for 头）不拆行且带尾空格；`, ` 统一带尾空格；`)` `]` `;` `}` 前都先去尾空白，`(` 仅在泛型闭合（行尾是 `>`）时收紧——否则 `x = (a + b)` 的空格会被吃掉；`}` 后跟 `) ] ; ,` 收尾链同行（lambda 的 `});`），`} catch (…) {` 靠"留在 line 缓冲"自然成行。
  - `minifyBraceLang.needSpace` 必须有 `prev === ">" && WORD.test(next)` 规则：否则 `List<int> x` 会被压成 `List<int>x`（语义破坏）。

### 自动识别（`detectLanguage`）

- 九语言加权打分，取最高分且 **≥ 3** 才判定；判定顺序 `json → xaml → html → xml → sql → cs → java → css → js`（同分先到先得，所以 XAML / HTML 压过 XML 的规则要写得比 XML 的分高，改分值时注意这个次序约束）。
- 字符串内容先被替换掉再打分（`tNoStr`），避免字符串里的关键字干扰识别。

### 主题 / 背景 / 存储

- 主题三态：`localStorage["forge-theme"]` = `light` / `dark`；无记录时跟随 `prefers-color-scheme` 并监听系统变化；`<head>` 里有一段**阻塞式内联脚本**先设 `data-theme` 再渲染，防止浅色用户看到深色闪屏——别把它合并进页面尾部的脚本。
- 背景 `localStorage["forge-bg"]` 存 `{color, image, opacity, glass, blur}` JSON；`normalizeBg` 负责取值归一（范围 0–100 / 0–40），配置读坏了回默认值。本地图转 dataURL，超 1920px 缩放 + JPEG 重压（0.82 → 0.65），防止 localStorage 爆掉。
- 复制 / 粘贴都是「`navigator.clipboard` 优先，`execCommand` 降级，再失败给出手动操作提示」的三层结构，改动时别把降级链弄丢。

### 调试钩子

`window.__fmt` 暴露 `detectLanguage` / `engines` / 主题与背景函数，供浏览器控制台抽查与冒烟脚本复用。

## 已知限制（K-3，见需求 6.2）

1. **K-3**：JS 正则字面量紧跟 `)` 之后被当除法（`if(a)/re/.test(b)` → `if (a) / re /.test(b)`）。`regexAllowed()` 的白名单不含 `)`——这是**权衡后的选择，不是待修 bug**：若放开 `)`，`(a+b)/c/d` 这类除法链会被 `/c/d` 误判成 正则，按下葫芦浮起瓢。规避：正则先赋给变量。
2. **K-4 / K-5**：CSS / 压缩路径丢注释是有意设计，不是 bug，不要"修复"。

> K-1（C#/Java 复合运算符拆散）与 K-2（泛型被当比较符）已于 2026-09-22 修复并纳入回归断言；修复方式与改动位置见上节"关键实现事实"，后续改 `formatBraceLang` 时别退化回去。

## 验证方式

```bash
# 引擎冒烟（36 条断言：九语言格式化 / 压缩 / 自动识别关键行为 + K-1/K-2 修复回归）
# 脚本直接从 CodeFormatter.html 切出真实引擎代码跑，没有第二份代码，不会漂移
node Code/scripts/smoke-formatter.js
```

- 冒烟全绿后**还必须浏览器人工过一遍**：主题切换、背景面板、双栏行高亮、复制粘贴降级——这些 DOM / 渲染层的部分脚本覆盖不了。
- `smoke-formatter.js` 靠两行锚点注释切引擎段：`/* ========== 语言列表 ========== */` 与 `/* ========== UI ========== */`。**改 HTML 时别动这两行注释**，挪动了同步改脚本。
- 新增引擎行为时先在冒烟脚本里加断言再改引擎（本仓库一贯的 TDD 习惯）。

## 文档

- `Docs/需求.md`（SRS v1.0，含已知限制 K-3 至 K-5；K-1/K-2 已修复，记录见"关键实现事实"）
- `README.md`（使用说明 + 已知限制 + 隐私口径）
