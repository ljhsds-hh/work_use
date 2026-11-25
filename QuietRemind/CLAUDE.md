# QuietRemind 工程关键记忆

## 项目核心需求（SRS 基线：Docs/需求.md）

- **定位**：Win11 桌面托盘常驻任务提醒工具，核心功能只有一个——提醒我有任务
- **技术栈**：C# / WPF / .NET 8 / HandyControl（UI 一切以 HandyControl 为核心，未提供才退用 WPF 原生；托盘图标允许成熟第三方方案）
- **提醒必达三路径闭环**：到点全屏提醒 → 关机前拦截提醒（欠账判定：今天还欠着 + 任务启用 + 未终态）→ 开机/唤醒补提醒（错过判定）
- **约束**：提醒全程无声音、无花瓣类装饰动画；提醒浮层必须逐条收尾才能关闭；关闭主窗口=最小化到托盘不退出；全程单实例；轻量原则（≤100MB、CPU≈0、无联网无广告无多余服务、日志 30 天滚动）
- **UI 设计规范**（2026-11-24 企业级重构 · Atelier 设计系统）：暖纸画布 `#F7F6F2`、墨色文字 `#15181C`、深松绿主色 `#1F5E52`、琥珀时间强调 `#B45309`、发丝线 `#E6E2DA`；左侧 64px 深色导航栏 `#141A18` + 中间任务工作区 + 可收起右侧设置检查器（340px）；字体优先 Segoe UI Variable，中文回退 Microsoft YaHei UI，数字使用 Bahnschrift；圆角 8/10/14；标题栏为 48px 深色自绘（主窗）或白色自绘（编辑/历史/快速新增）；全部令牌、按钮、输入、开关、分段选择、星期圆片、滑杆、表格样式集中在 `Themes/DesignTokens.xaml`，并覆盖 HandyControl 的 `PrimaryBrush/PrimaryColor` 等资源为松绿，保证 HC 控件焦点/选中色一致；提醒仍为**主屏全屏暗化遮罩 + 居中卡片**（必达醒目性优先），无声音、无装饰性动画
- **UI 控件约束**：HandyControl 仍是控件底座（HC 优先），视觉通过统一 Style/模板覆盖实现；新增控件先复用 DesignTokens 现有键，禁止在窗口内散落硬编码色值；标题栏按钮必须设置 `WindowChrome.IsHitTestVisibleInChrome="True"`；分段单选使用 SegmentRadio + 代码同步初始选中态（不要仅靠 IsChecked 单向绑定 + GroupName）
- **数据**：`%AppData%\QuietRemind\`（JSON 原子写；解析损坏移动 `.corrupt-*` 备份后重建，读取失败保留原文件并本次会话禁写）；**日志**：`D:\logs\QuietRemind\` 按日分文件；**退出标记 exit.marker 存日志目录**（部分环境计划任务进程对 %AppData% 新文件有视图隔离，日志目录两边视图一致，实测验证）

## 关键实现机制（改动前必读）

1. **核心状态机**（ReminderEngine）：
   - 实例状态：Pending / Missed / Completed / Skipped；终态不可逆
   - 触发判定：`Pending && TriggerAt<=now && TriggerAt>_lastPoll && !runtimeShown` → 正常提醒（不改状态）
   - 错过扫描（启动/唤醒/轮询）：`Pending && TriggerAt<=cutoff && TriggerAt<now && !runtimeShown` → 判 Missed → 补提醒
   - **runtimeShown 是内存态**（进程重启即失效）：这是"运行期正常到点不误判错过"与"强杀后已弹未收尾仍补提醒"同时成立的机制基础，改动时保持该性质
   - 时钟向前大跳（墙钟 >10s 且单调时钟推进 <5s）→ 全量错过扫描兜底；UI 卡顿/睡眠的大间隔走常规到点判定；首轮 _lastPoll 初始化为 now-2s
   - **提醒弹出本身不改变实例状态，仅收尾动作变更**（Settle）
2. **关机拦截**：App.SessionEnding 处理器内同步 ShowDialog 拦截窗（消息泵运行、系统等待回复）；**ShowDialog 返回后立即写 e.Cancel**；【取消关机】→ e.Cancel=true；【仍要关机】→ 放行（不重发关机指令，天然避免反复拦截）；无欠账静默放行；拦截期间新提醒暂存，取消后先展示成功再清空（失败保留并由轮询每 5s 重试）
3. **实例滚动生成**（OccurrencePlanner）：[生效起始日, +7天) 半开区间（StartDate 晚于今天则从 StartDate 起，且不早于 notBefore 规划下限）；去重仅未收尾实例占位；当天已过时刻不追溯（2.3.1）；**单次任务不受 7 天窗口限制，设定即生成**（避免长期未运行后日期滑出窗口而静默丢失）；**编辑用 RebuildFuture：删除该任务全部未收尾实例（含已触发/已错过/snooze 中）按新设置重建并立即生效**（2026-09-19 用户澄清：编辑保存后当天即按新时刻触发），已收尾历史不动；**停用任务的未收尾实例统一置为已跳过**，避免重新启用时集中补“已错过”历史欠账；**AppSettings.LastPlannedDate 为规划水位**：系统日期回拨 ≤7 天（与规划窗口一致）时跳过补生成防止重复，回拨幅度过大视为历史前跳残留自动重置；**2026-11-24 循环扩展**：新增 Yearly（每年 Month 月 MonthDay 日，只生成下一次且不受 7 天窗口限制，平年 2/29 取 2/28）、Interval（每 N 天/周/月，>1 时以 StartDate 为锚点，编辑器强制填写；脏数据无锚点按每 1 周期处理并警告）、EndDate（含当天；所有循环生效）
4. **计划任务守护**（TaskSchedulerGuard）：任务名 `QuietRemind`，LogonTrigger 优先 + 每 5 分钟 TimeTrigger（起点=注册+5min，避免当天空窗），动作统一带 `--scheduled` 参数，InteractiveToken 需显式传 userId；**LogonTrigger 被拒时自动降级为仅守护触发**并记日志；**登录/退出语义由当前进程令牌的登录会话 LUID（`TokenStatistics.AuthenticationId`）判定**：exit.marker 记录写入时会话 LUID，与当前一致→守护触发静默退出，不一致/旧格式→清除并恢复提醒；同会话手动/计划任务/提权进程 LUID 一致，注销重登/重启/快速启动重新登录后变化；注册用 CreateOrUpdate 原地覆盖，失败不再丢失旧任务；`GetStatus()` 供运行诊断按需查询（不进入轮询）
5. **启动编排**（App.xaml.cs）：`--restart-after=<pid>`（备份导入后自重启，先等旧进程退出再抢单实例）→ Mutex（Global 优先，Local 回退）→ 退出标记按“当前登录会话”判定 → 数据加载 + 归一化（解析损坏移动 `.corrupt-*` 备份后重建；读取失败保留原文件并本次会话拒写提示）→ **每日快照（同日一次，保留 5 份）** → 守护注册/注销 reconcile → **快速新增热键注册（Ctrl+Alt+N）** → 停用清理 + 实例补齐（规划水位 notBefore）落盘 → 错过扫描 → **提前提醒补扫** → 1s DispatcherTimer 轮询（含单实例唤醒信号消费，无常驻后台线程）→ 托盘；`--scheduled` 静默进托盘，手动启动显示主窗口；日志目录 D: 不可写时整体回退 `%AppData%\QuietRemind\logs`（含 exit.marker/mutex.ns）；**睡眠 Suspend 仅打 `_resumeScanRequired` 标记（不停轮询，避免 Resume 丢失后永久停摆），唤醒首轮先错过扫描/提前提醒再常规触发**
6. **单实例唤醒**：命名事件 QuietRemind_ShowMain（Global 优先 Local 回退），手动重复启动时唤起已有实例主界面；已有实例在轮询中消费信号
7. **删除任务**：实例全部保留（需求 2.3.3/8.3），引擎/拦截按"任务列表无此任务"自然过滤；主界面提供 5 秒撤销（单次 DispatcherTimer，恢复原位置并补齐未来实例，未收尾实例重新参与提醒）
8. **辅助功能（2026-11-24）**：历史记录窗口只读最近 200 条；运行诊断卡按需查询计划任务/日志/落盘错误；提醒预览复用主屏全屏样式但可关闭且不落盘；快速新增（热键+轻量窗）走现有 Planner；数据备份单一 JSON 导出/导入（导入前自动备份并重启），启动每日快照；提前提醒默认为 0（关闭），>0 时仅托盘气泡（`PreAlertShownAt` 幂等，跳过 snooze/停用），**绝不改变主屏全屏提醒形态**

## 开发规范

- 服务层零 UI 依赖；时间一律经 `IClock` 注入（测试用 FakeClock）
- 任何状态变更必须 `AppServices.Persist()` 实时落盘
- 提醒遮罩+卡片禁止被 ESC/Alt+F4 关闭（Closing 里 e.Cancel）；卡片屏幕居中
- 新功能优先补齐单元测试（现有 93 用例）；测试工程 net8.0-windows + UseWPF 引用主工程
- 发布：Release 单文件自包含（win-x64 压缩），见 Docs/发布部署指南.md
- 图标由 Code/scripts/gen-icon.ps1 程序化生成，勿手工编辑 app.ico

## 文档

- Docs/需求.md（SRS 基线）、Docs/设计文档.md（架构与状态机）、Docs/测试报告.md、Docs/评审记录.md
