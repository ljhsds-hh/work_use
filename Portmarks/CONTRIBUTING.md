# 团队规范

适用于本仓库的所有改动。规则不是为了整齐，是为了避免已经踩过的坑再踩一次——每条后面几乎都对应一次真实故障。

目录：

- [一、Git 规范](#一git-规范)
- [二、编码规范](#二编码规范)
- [三、评审清单](#三评审清单)
- [四、这个项目的四个坑](#四这个项目的四个坑)

---

## 一、Git 规范

### 1. 分支模型

个人开发可以只在 `main` 上走，但只要超过一个人，就按下面来：

| 分支 | 用途 | 生命周期 |
|---|---|---|
| `main` | 随时可发布，永远可运行 | 长期 |
| `feat/<简述>` | 新功能 | 合并后删除 |
| `fix/<简述>` | 缺陷修复 | 合并后删除 |
| `chore/<简述>` | 构建、依赖、脚手架 | 合并后删除 |
| `docs/<简述>` | 只改文档 | 合并后删除 |

- 分支名小写，用连字符：`feat/accent-themes`、`fix/scrim-slider-id`
- **禁止直接 push `main`**。所有改动走 PR，至少一人 review
- 合并用 **squash merge**，一个 PR 在 `main` 上只留一个提交

### 2. 提交信息

格式对齐 [Conventional Commits](https://www.conventionalcommits.org/)：

```
<type>(<scope>): <subject>

<body>

<footer>
```

**type**（只能取这几个）

| type | 含义 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修 bug |
| `refactor` | 重构，不改变外部行为 |
| `perf` | 性能优化 |
| `test` | 只动测试 |
| `docs` | 只动文档 |
| `style` | 格式、空白、分号（不改逻辑） |
| `chore` | 依赖、脚本、配置 |
| `build` | 构建产物相关 |

**scope**（本仓库固定这几个）

`core`（纯逻辑）· `store`（文件读写）· `server`（HTTP 层）· `ui`（前端）· `test` · `docs`

**subject**

- 中文，动词开头，说清"做了什么"
- 不超过 50 个字符，末尾不加句号
- 不要写"修改了一些问题"这种信息量为零的句子

**body**（可选，但下列情况必须写）

- 修复类：写清**根因**，不是现象
- 行为变更：写清对使用者的影响
- 有副作用：写清楚

**footer**

- 破坏性变更：`BREAKING CHANGE: 说明`
- 关联任务：`Refs: #12` / `Closes: #12`

### 3. 好例子（全部来自本仓库真实提交）

```
feat(ui): 增加 7 套主题色与背景图自定义

fix(store): 目标文件损坏时返回 422，不再抛裸 JSON 解析错误

fix(ui): 遮罩滑块无效——遮罩层与滑块共用 id，监听器挂到了 div 上

refactor(core): 去重键改为按目标父文件夹共享，修复同名文件夹合并时重复建书签

perf(store): 写入改为先写临时文件再改名，避免进程中断留下半截 JSON

docs: 补充跨机器搬运（导出/导入）使用说明
```

反面例子：

```
update code          ← 不知道改了什么
fix bug              ← 不知道修了哪
修复了一些问题，优化了体验   ← 信息量为零
```

### 4. 提交粒度

- **一次提交只做一件事**。"修 bug + 顺手改格式"必须拆开，否则 review 和回滚都失效
- 不要把格式化、重命名和逻辑改动混在一个提交里
- 提交前必须能独立通过 `npm test`
- 大改动的迁移与行为变更分开提交，便于二分定位

### 5. 版本与标签

语义化版本 `MAJOR.MINOR.PATCH`：

- `MAJOR`：数据格式或行为不兼容（例如 `.portmarks` 升版本）
- `MINOR`：新增功能
- `PATCH`：修 bug

打标签：`git tag -a v1.2.0 -m "v1.2.0"`，标签信息里写清变更要点。

### 6. PR 要求

- 标题直接用提交信息的 subject 格式
- 描述里写三件事：**为什么改、改了什么、怎么验证的**
- 有界面的改动，附改动前后截图
- CI / 本地测试必须绿

---

## 二、编码规范

### 1. 分层职责（最重要的一条）

```
src/core/          纯函数。禁止出现 fs / http / chrome / DOM
src/browser-store  文件读写、profile 发现、进程检测
src/server.js      HTTP 路由与入参校验
public/            界面：只关心状态与渲染
```

**为什么**：`core` 不依赖任何运行环境，所以能用 Node 直接跑单测，不用起浏览器。一旦有人往 `core` 里塞一个 `fs.readFileSync`，整层测试就废了。

### 2. 命名

| 种类 | 规则 | 例子 |
|---|---|---|
| 变量/函数 | camelCase，函数动词开头 | `buildPlan` `applyPlan` `readTree` |
| 常量 | UPPER_SNAKE | `PERMANENT_GUIDS` `LOOK_KEY` |
| 布尔 | `is` / `has` / `can` 开头 | `getRunning` ❌ → `isRunning` ✅ |
| 类 | PascalCase | `PackError` |
| 私有/内部 | 不加下划线，靠模块边界 | `rootsOf` `writeRaw` |

**用全名，别缩写**：`src` → `source`、`tgt` → `target`、`btn` → `button`（局部极短作用域可放宽）。

### 3. 错误处理

- 抛错时带上 HTTP 语义的状态码：`err.status = 422`，服务端统一读取，不要在每个路由里 if/else
- 面向用户的错误信息说人话：
  - ❌ `Unexpected token < in JSON at position 0`
  - ✅ `收藏夹文件无法解析（Bookmarks）：...`
- **禁止空 catch**。要么处理，要么写明为什么可以忽略：

```js
try {
  fs.unlinkSync(tmp);
} catch {
  /* 临时文件可能压根没生成，忽略 */
}
```

- 批处理要容错：单条失败不能中断整体（见 `applyPlan`），失败项收集到 `errors` 返回给界面

### 4. 文件 IO 铁律

1. **写任何用户数据前先备份**：`backupFileFor()` + `fs.copyFileSync`
2. **写入必须原子**：先写 `xxx.pmtmp`，再 `rename`。直接覆盖时进程被杀 = 文件废掉
3. **只读源，不写源**：拷贝场景下源文件永远只读
4. 备份文件名要防撞：同一秒内连续操作不能覆盖上一份

### 5. 安全

- 前端渲染用户数据**只准用 `textContent`**，禁止 `innerHTML`（书签标题就是用户输入）
- 服务端只监听 `127.0.0.1`；校验 `Host`（防 DNS rebinding）、校验 `Origin`、写接口要求自定义头 `x-pm-client`
- 静态文件用 `path.relative()` 判断越界，不要用 `startsWith`（`public-evil` 会被误判为 `public`）

### 6. 浏览器数据格式

- **顶层容器的 GUID 是写死的常量**，用随机 UUID 会让浏览器静默忽略整个文件（不报错、不改文件、API 返回空）

```js
bookmark_bar: '0bc5d13f-2cba-5d74-951f-3f233fe6c908'
other:        '82b081ec-3dd3-529c-8475-ab6c344590dd'
synced:       '4cf2e351-0e85-532b-bb37-df045d8f8d0f'
```

- 时间戳是 **1601 年起的微秒**，必须走 `nowChromeTime()`，不要手算
- 新建节点需要 `id`（全树最大 id + 1）、`guid`、`date_added`；`url` 节点带 `meta_info`，`folder` 节点带 `date_modified`

### 7. 前端约定

- **改完状态先刷新视图，再写提示**。反了的话提示会被重新渲染擦掉（这是真实踩过的 bug）
- **HTML id 必须唯一**。重复 id 时 `getElementById` 只返回第一个，监听器会静默挂到错的元素上——`test/markup.test.js` 会拦
- 事件绑定集中在 `bind()` / `initXxx()`，不要散落在渲染函数里
- 面板打开后焦点进面板，Esc 关闭，关闭后焦点还给触发按钮
- 颜色、间距一律走 CSS 变量，禁止在组件里写死色值
- 深浅主题都要看一眼，新增颜色必须两套都给

### 8. 测试

| 改了什么 | 必须补什么 |
|---|---|
| `core` 的合并/规划逻辑 | `test/core.test.js` 用例 |
| 文件读写、备份、导入导出 | `test/store.test.js` 用例 |
| HTML 结构、id | 跑 `npm test`（含 markup 检查） |
| 界面交互 | `tools/shoot-ui.mjs` 截图自查，重要流程手动过一遍 |

- 测试用真实结构造数据（`bookmarkFile()` 辅助函数），不要用假对象骗过断言
- 断言要断到字段级，不要只断 `assert.ok(result)`

### 9. 注释

只写"为什么"，不写"是什么"：

```js
// ❌ 遍历根节点
// ✅ 只取两个可写容器：Chromium 还有 synced / workspaces 等只读容器，写进去会失败
```

---

## 三、评审清单

提 PR 前自己先过一遍，reviewer 也按这个看：

- [ ] `npm test` 全绿（34 项）
- [ ] 前端调用的接口在 `src/server.js` 里都有；服务端路由都有调用方
- [ ] 没有 `innerHTML` / `eval` / `new Function`
- [ ] 新增的写文件路径有备份 + 原子写
- [ ] HTML 没有重复 id
- [ ] 深浅两套主题都实际看过
- [ ] 键盘可操作：Tab 能到、Esc 能关
- [ ] 错误场景手动试过：文件损坏、目标在运行、空数据
- [ ] 提交信息符合规范，一次提交只做一件事

---

## 四、这个项目的四个坑

都是真发生过的，写在这里省得再踩：

**1. 浏览器的 `.bak` 不一定会救你**
删掉 `Bookmarks` 只留 `Bookmarks.bak`，Chrome 启动后加载的是**空树**，不会从 `.bak` 恢复。所以别指望浏览器自带的备份兜底。

**2. 账号同步会推翻文件级删除**
目标浏览器登录了账号并开启同步时，删本地文件后浏览器一启动就把云端数据拉回来。**复制没问题，删除打不过云端**。工具因此去掉了回滚功能。

**3. 重复 id 让控件静默失效**
遮罩层和滑块同名 `bgScrim`，结果滑块拖不动、也不报错。凡是"操作没反应"的 bug，先查 id。

**4. 成功提示被自己擦掉**
`setStatus('完成')` 之后又 `await load()`，而 `load()` 内部重算差异时会清空状态栏。正确顺序是**先刷新、后写提示**。
