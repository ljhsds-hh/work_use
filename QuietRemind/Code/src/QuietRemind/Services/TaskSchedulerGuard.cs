using Microsoft.Win32.TaskScheduler;

namespace QuietRemind.Services;

/// <summary>
/// 进程守护（需求 7 章）：注册/注销名为 QuietRemind 的计划任务。
/// 触发器：登录触发 + 每 5 分钟守护触发；MultipleInstances.IgnoreNew 与 Mutex 双保险；
/// InteractiveToken 保证在用户桌面会话运行（Session 0 无法弹窗，见需求 7 章背景说明）。
/// </summary>
public sealed class TaskSchedulerGuard
{
    public const string TaskName = "QuietRemind";
    public const string ScheduledArg = "--scheduled";

    private readonly string _exePath;
    private readonly LogService _log;

    public TaskSchedulerGuard(string exePath, LogService log)
    {
        _exePath = exePath;
        _log = log;
    }

    public bool EnsureRegistered()
    {
        try
        {
            using var svc = new TaskService();
            if (svc.FindTask(TaskName) is { } existing && SameConfig(existing, _exePath))
            {
                _log.Info("计划任务已注册且配置一致，跳过");
                return true;
            }

            // 优先完整配置（登录触发 + 守护触发）；环境拒绝 LogonTrigger 时降级为仅守护触发。
            // 使用 CreateOrUpdate 原地覆盖，不再先删除：注册失败时旧任务仍保留，不会出现无守护窗口。
            // SameConfig 要求登录触发器存在：环境恢复后每次启动都会重试完整配置，不会永久停留降级态。
            var td = BuildDefinition(svc, includeLogonTrigger: true);
            if (!TryRegister(svc, td))
            {
                td = BuildDefinition(svc, includeLogonTrigger: false);
                if (!TryRegister(svc, td))
                {
                    return false;
                }
                _log.Warn("当前环境不允许登录触发器，已降级为仅守护触发（开机后最迟 5 分钟自动启动）");
            }

            _log.Info("计划任务注册成功");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("计划任务注册失败", ex);
            return false;
        }
    }

    private TaskDefinition BuildDefinition(TaskService svc, bool includeLogonTrigger)
    {
        var td = svc.NewTask();
        td.RegistrationInfo.Description = "QuietRemind 开机自启动与进程守护";
        td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        td.Settings.Enabled = true;
        td.Settings.StartWhenAvailable = true;
        td.Settings.Hidden = true;

        if (includeLogonTrigger)
        {
            // 登录触发（开机自启动）
            td.Triggers.Add(new LogonTrigger());
        }

        // 守护触发：每 5 分钟重复，无限期（需求 7.1.2）；起点 = 注册后 5 分钟，
        // 避免固定次日起点导致注册当天存在近 24 小时无守护触发的空窗
        td.Triggers.Add(new TimeTrigger
        {
            StartBoundary = RoundToMinute(DateTime.Now.AddMinutes(5)),
            Repetition = { Interval = TimeSpan.FromMinutes(5), Duration = TimeSpan.Zero },
        });

        // 动作统一带 --scheduled 参数：进程内据此区分"计划任务启动"与"用户手动双击"
        td.Actions.Add(new ExecAction(_exePath, ScheduledArg, null));
        return td;
    }

    private bool TryRegister(TaskService svc, TaskDefinition td)
    {
        try
        {
            // InteractiveToken 需显式指定当前用户（null userId 会被拒绝访问）
            var userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            svc.RootFolder.RegisterTaskDefinition(
                TaskName, td, TaskCreation.CreateOrUpdate, userId, null, TaskLogonType.InteractiveToken);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn($"计划任务注册尝试失败（将评估降级）：{ex.Message}");
            return false;
        }
    }

    public bool Unregister()
    {
        try
        {
            using var svc = new TaskService();
            // FindTask 在任务不存在时返回 null；GetTask 在部分版本会抛 FileNotFoundException
            if (svc.FindTask(TaskName) is null)
            {
                return true;
            }
            svc.RootFolder.DeleteTask(TaskName);
            _log.Info("计划任务已注销");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("计划任务注销失败", ex);
            return false;
        }
    }

    /// <summary>守护状态快照：仅诊断界面按需查询，不进入秒级轮询。</summary>
    public sealed record GuardStatus(
        bool TaskExists,
        bool Enabled,
        bool HasLogonTrigger,
        bool HasGuardTrigger,
        bool ActionMatches,
        DateTime? LastRunTime,
        int? LastTaskResult,
        string Summary);

    /// <summary>查询计划任务注册现状（只读，不修改任务）。</summary>
    public GuardStatus GetStatus()
    {
        try
        {
            using var svc = new TaskService();
            var task = svc.FindTask(TaskName);
            if (task is null)
            {
                return new GuardStatus(false, false, false, false, false, null, null,
                    "未注册（开机自启与进程守护未生效）");
            }

            var action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
            var actionMatches = action is not null
                && action.Arguments?.Contains(ScheduledArg, StringComparison.OrdinalIgnoreCase) == true
                && string.Equals(action.Path?.Trim('"'), _exePath.Trim('"'), StringComparison.OrdinalIgnoreCase);
            var hasLogon = task.Definition.Triggers.Any(t => t is LogonTrigger && t.Enabled);
            var hasGuard = task.Definition.Triggers.OfType<TimeTrigger>()
                .Any(t => t.Enabled && t.Repetition.Interval == TimeSpan.FromMinutes(5));

            DateTime? lastRun = null;
            int? lastResult = null;
            try
            {
                lastRun = task.LastRunTime;
                lastResult = task.LastTaskResult;
            }
            catch (Exception)
            {
                // 部分系统/权限组合不允许读取运行历史，不影响配置诊断
            }

            var summary = !task.Enabled
                ? "已注册但任务被禁用，建议修复"
                : !actionMatches
                    ? "已注册但启动配置不一致，建议修复"
                    : !hasGuard
                        ? "已注册但缺少守护触发，建议修复"
                        : !hasLogon
                            ? "已注册（降级：仅每 5 分钟守护）"
                            : "已注册且配置一致";

            return new GuardStatus(true, task.Enabled, hasLogon, hasGuard, actionMatches,
                lastRun, lastResult, summary);
        }
        catch (Exception ex)
        {
            _log.Warn($"查询计划任务状态失败：{ex.Message}");
            return new GuardStatus(false, false, false, false, false, null, null,
                $"状态查询失败：{ex.Message}");
        }
    }

    private static bool SameConfig(Microsoft.Win32.TaskScheduler.Task task, string exePath)
    {
        // 完整配置校验：动作路径/参数一致 + 任务启用 + 登录触发器存在 + 守护触发器每 5 分钟重复。
        // 降级任务（无登录触发器）或外部被改动（缺 --scheduled、被禁用）不视为一致，下次启动会重建。
        var action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
        var hasScheduledArg = action?.Arguments is { } args
            && args.Contains(ScheduledArg, StringComparison.OrdinalIgnoreCase);
        var hasLogonTrigger = task.Definition.Triggers.Any(t => t is LogonTrigger && t.Enabled);
        var guardTrigger = task.Definition.Triggers.OfType<TimeTrigger>()
            .Any(t => t.Enabled && t.Repetition.Interval == TimeSpan.FromMinutes(5));
        return action is not null
            && hasScheduledArg
            && task.Enabled
            && hasLogonTrigger
            && guardTrigger
            && string.Equals(action.Path?.Trim('"'), exePath.Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime RoundToMinute(DateTime time)
        => new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
}
