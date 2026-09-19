# QuietRemind 工程关键记忆

## 项目核心需求（SRS 基线：Docs/需求.md）

- **定位**：Win11 桌面托盘常驻任务提醒工具，核心功能只有一个——提醒我有任务
- **技术栈**：C# / WPF / .NET 8 / HandyControl（UI 一切以 HandyControl 为核心，未提供才退用 WPF 原生；托盘图标允许成熟第三方方案）
- **提醒必达三路径闭环**：到点全屏提醒 → 关机前拦截提醒（欠账判定：今天还欠着 + 任务启用 + 未终态）→ 开机/唤醒补提醒（错过判定）
- **约束**：提醒全程无声音、无花瓣类装饰动画；全屏遮罩窗口必须逐条收尾才能关闭；关闭主窗口=最小化到托盘不退出；全程单实例；轻量原则（≤100MB、CPU≈0、无联网无广告无多余服务、日志 30 天滚动）
- **数据**：`%AppData%\QuietRemind\`（JSON 原子写）；**日志**：`D:\logs\QuietRemind\` 按日分文件；**退出标记 exit.marker 存日志目录**（部分环境计划任务进程对 %AppData% 新文件有视图隔离，日志目录两边视图一致，实测验证）

## 关键实现机制（改动前必读）

1. **核心状态机**（ReminderEngine）：
   - 实例状态：Pending / Missed / Completed / Skipped；终态不可逆
   - 触发判定：`Pending && TriggerAt<=now && TriggerAt>_lastPoll && !runtimeShown` → 正常提醒（不改状态）
   - 错过扫描（启动/唤醒/轮询）：`Pending && TriggerAt<=cutoff && TriggerAt<now && !runtimeShown` → 判 Missed → 补提醒
   - **runtimeShown 是内存态**（进程重启即失效）：这是"运行期正常到点不误判错过"与"强杀后已弹未收尾仍补提醒"同时成立的机制基础，改动时保持该性质
   - 时钟向前大跳（>10s 且非睡眠）→ 全量错过扫描兜底；首轮 _lastPoll 初始化为 now-2s
   - **提醒弹出本身不改变实例状态，仅收尾动作变更**（Settle）
2. **关机拦截**：App.SessionEnding 处理器内同步 ShowDialog 拦截窗（消息泵运行、系统等待回复）；【取消关机】→ e.Cancel=true；【仍要关机】→ 放行（不重发关机指令，天然避免反复拦截）；无欠账静默放行
3. **实例滚动生成**（OccurrencePlanner）：[今天, +7天) 半开区间；按 OriginalTriggerAt 去重（snooze 只改 TriggerAt）；当天已过时刻不追溯（2.3.1）；编辑用 RebuildFuture（删 Pending 未弹实例重建，不碰已收尾历史）
4. **计划任务守护**（TaskSchedulerGuard）：任务名 `QuietRemind`，LogonTrigger 优先 + 每 5 分钟 TimeTrigger（起点=注册+5min，避免当天空窗），动作统一带 `--scheduled` 参数，InteractiveToken 需显式传 userId；**LogonTrigger 被拒时自动降级为仅守护触发**并记日志，登录语义由进程内 IsRecentBootStartup（系统启动 2 分钟内）等价保障；SameAction 幂等校验含守护触发器 5 分钟重复配置
5. **启动编排**（App.xaml.cs）：Mutex（Global\QuietRemind 优先，受限环境回退 Local\）→ 退出标记判定（守护语义遇标记静默退出；登录/手动语义清除）→ 数据加载（损坏备份后空数据启动）→ 守护注册 → 实例补齐落盘 → 错过扫描（判错过即落盘）→ 1s DispatcherTimer 轮询（含单实例唤醒信号消费，无常驻后台线程）→ 托盘；`--scheduled` 静默进托盘，手动启动显示主窗口
6. **单实例唤醒**：命名事件 QuietRemind_ShowMain（Global 优先 Local 回退），手动重复启动时唤起已有实例主界面；已有实例在轮询中消费信号
7. **删除任务**：实例全部保留（需求 2.3.3/8.3），引擎/拦截按"任务列表无此任务"自然过滤

## 开发规范

- 服务层零 UI 依赖；时间一律经 `IClock` 注入（测试用 FakeClock）
- 任何状态变更必须 `AppServices.Persist()` 实时落盘
- 提醒窗口禁止被 ESC/Alt+F4/遮罩点击关闭（Closing 里 e.Cancel）
- 新功能优先补齐单元测试（现有 39 用例）；测试工程 net8.0-windows + UseWPF 引用主工程
- 发布：Release 单文件自包含（win-x64 压缩），见 Docs/发布部署指南.md
- 图标由 Code/scripts/gen-icon.ps1 程序化生成，勿手工编辑 app.ico

## 文档

- Docs/需求.md（SRS 基线）、Docs/设计文档.md（架构与状态机）、Docs/测试报告.md、Docs/评审记录.md
