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

    public bool IsRegistered()
    {
        try
        {
            using var svc = new TaskService();
            return svc.FindTask(TaskName) is not null;
        }
        catch (Exception ex)
        {
            _log.Error("查询计划任务失败", ex);
            return false;
        }
    }

    public bool EnsureRegistered()
    {
        try
        {
            using var svc = new TaskService();
            if (svc.FindTask(TaskName) is { } existing && SameAction(existing, _exePath))
            {
                _log.Info("计划任务已注册且配置一致，跳过");
                return true;
            }

            DeleteIfExists(svc);

            var td = svc.NewTask();
            td.RegistrationInfo.Description = "QuietRemind 开机自启动与进程守护";
            td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            td.Settings.Enabled = true;
            td.Settings.StartWhenAvailable = true;
            td.Settings.Hidden = true;

            // 登录触发（开机自启动）
            td.Triggers.Add(new LogonTrigger());

            // 守护触发：每 5 分钟重复，无限期（需求 7.1.2）
            td.Triggers.Add(new TimeTrigger
            {
                StartBoundary = DateTime.Today.AddDays(1).AddMinutes(5),
                Repetition = { Interval = TimeSpan.FromMinutes(5), Duration = TimeSpan.Zero },
            });

            // 动作统一带 --scheduled 参数：进程内据此区分"计划任务启动"与"用户手动双击"
            td.Actions.Add(new ExecAction(_exePath, ScheduledArg, null));

            svc.RootFolder.RegisterTaskDefinition(
                TaskName, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);

            _log.Info("计划任务注册成功");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("计划任务注册失败", ex);
            return false;
        }
    }

    public void Unregister()
    {
        try
        {
            using var svc = new TaskService();
            DeleteIfExists(svc);
            _log.Info("计划任务已注销");
        }
        catch (Exception ex)
        {
            _log.Error("计划任务注销失败", ex);
        }
    }

    private static void DeleteIfExists(TaskService svc)
    {
        try
        {
            svc.RootFolder.DeleteTask(TaskName);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // 任务不存在或无权删除：忽略，交由注册逻辑处理
        }
    }

    private static bool SameAction(Microsoft.Win32.TaskScheduler.Task task, string exePath)
    {
        var action = task.Definition.Actions.FirstOrDefault(a => a is ExecAction) as ExecAction;
        return action is not null
            && string.Equals(action.Path?.Trim('"'), exePath.Trim('"'), StringComparison.OrdinalIgnoreCase);
    }
}
