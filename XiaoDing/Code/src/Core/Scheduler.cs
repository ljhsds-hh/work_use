using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace XiaoDing.Core
{
    public sealed class TaskDueEventArgs : EventArgs
    {
        public TaskDueEventArgs(TodoTask task, bool isLate)
        {
            Task = task;
            IsLate = isLate;
        }

        public TodoTask Task { get; private set; }

        /// <summary>是否在到期之后才触发（例如应用未运行、电脑休眠）。</summary>
        public bool IsLate { get; private set; }
    }

    /// <summary>
    /// 提醒调度。使用固定心跳扫描全部任务，避免逐条设置定时器带来的漂移与泄漏；
    /// 唤醒、解锁、系统时间变化时立即补扫一次。
    /// </summary>
    public sealed class Scheduler : IDisposable
    {
        private const int TickIntervalMs = 15000;
        private static readonly TimeSpan LateThreshold = TimeSpan.FromMinutes(1);

        private readonly System.Windows.Forms.Timer _timer;
        private readonly Func<List<TodoTask>> _taskSource;
        private readonly HashSet<Guid> _firing = new HashSet<Guid>();
        private SynchronizationContext _sync;
        private bool _disposed;

        public Scheduler(Func<List<TodoTask>> taskSource)
        {
            if (taskSource == null)
            {
                throw new ArgumentNullException("taskSource");
            }

            _taskSource = taskSource;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = TickIntervalMs;
            _timer.Tick += OnTimerTick;

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.TimeChanged += OnTimeChanged;
        }

        public event EventHandler<TaskDueEventArgs> TaskDue;

        public bool Paused { get; set; }

        public void Start()
        {
            // 记录 UI 线程上下文：系统事件（唤醒/解锁/改时间）在其它线程触发，必须切回 UI 线程扫描，
            // 否则会和心跳并发读写任务列表、并在非 UI 线程创建窗口。
            _sync = SynchronizationContext.Current;
            _timer.Start();
            Tick();
        }

        /// <summary>把扫描切回创建调度器的线程执行。</summary>
        private void RunTick()
        {
            SynchronizationContext sync = _sync;
            if (sync != null && sync != SynchronizationContext.Current)
            {
                sync.Post(delegate { Tick(); }, null);
                return;
            }

            Tick();
        }

        /// <summary>立即扫描一次。UI 线程调用。</summary>
        public void Tick()
        {
            if (_disposed || Paused)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            List<TodoTask> tasks = _taskSource();
            if (tasks == null)
            {
                return;
            }

            // 复制一份，避免事件处理期间修改集合。
            List<TodoTask> candidates = new List<TodoTask>();
            foreach (TodoTask task in tasks)
            {
                if (task == null || !task.IsActive)
                {
                    continue;
                }

                DateTime fireUtc = task.GetFireUtc();
                if (fireUtc > now)
                {
                    continue;
                }

                if (task.LastFiredUtc.HasValue && task.LastFiredUtc.Value >= fireUtc)
                {
                    continue;
                }

                if (_firing.Contains(task.Id))
                {
                    continue;
                }

                task.LastFiredUtc = now;
                candidates.Add(task);
            }

            foreach (TodoTask task in candidates)
            {
                bool isLate = task.DueUtc < now.Subtract(LateThreshold);
                if (isLate && task.State == TaskState.Pending)
                {
                    task.State = TaskState.Missed;
                }

                _firing.Add(task.Id);
                try
                {
                    EventHandler<TaskDueEventArgs> handler = TaskDue;
                    if (handler != null)
                    {
                        handler(this, new TaskDueEventArgs(task, isLate));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Scheduler.TaskDue.HandlerFailed", ex);
                }
                finally
                {
                    _firing.Remove(task.Id);
                }
            }
        }

        /// <summary>某一任务的本次提醒已被处理，允许它在下个周期再次触发。</summary>
        public void ResetFiringState(Guid taskId)
        {
            _firing.Remove(taskId);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            Tick();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                RunTick();
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                RunTick();
            }
        }

        private void OnTimeChanged(object sender, EventArgs e)
        {
            RunTick();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.TimeChanged -= OnTimeChanged;

            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
        }
    }
}
