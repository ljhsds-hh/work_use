using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using XiaoDing.UI;

namespace XiaoDing.Core
{
    /// <summary>应用主上下文：串联存储、调度、托盘与各个窗口。</summary>
    internal sealed class XiaoDingAppContext : ApplicationContext
    {
        private readonly SingleInstance _singleInstance;
        private readonly TaskStore _store = new TaskStore();
        private readonly SettingsStore _settingsStore = new SettingsStore();
        private readonly List<TodoTask> _tasks;
        private readonly Dictionary<Guid, Form> _openReminders = new Dictionary<Guid, Form>();
        private readonly Queue<TodoTask> _overlayQueue = new Queue<TodoTask>();
        private readonly Queue<TodoTask> _cardQueue = new Queue<TodoTask>();

        private AppSettings _settings;
        private Scheduler _scheduler;
        private TrayIconHost _tray;
        private MainForm _mainForm;
        private ActivationWindow _activationWindow;
        private OverlayForm _activeOverlay;
        private ReminderCardForm _activeCard;
        private GlobalHotkey _hotkey;
        private QuickAddForm _quickAdd;
        private bool _syncing;
        private bool _exiting;

        public XiaoDingAppContext(SingleInstance singleInstance, bool silentStart)
        {
            _singleInstance = singleInstance;

            _tasks = _store.Load();
            _settings = _settingsStore.Load();
            Theme.ApplyTheme(_settings.DarkMode);

            _tray = new TrayIconHost();
            _tray.Paused = _settings.Paused;
            _tray.OpenRequested += delegate { ShowMain(); };
            _tray.NewTaskRequested += delegate { CreateTask(); };
            _tray.PauseChanged += OnTrayPauseChanged;
            _tray.ExitRequested += delegate { ExitApplication(); };

            _scheduler = new Scheduler(delegate { return _tasks; });
            _scheduler.Paused = _settings.Paused;
            _scheduler.TaskDue += OnTaskDue;

            _activationWindow = new ActivationWindow(_singleInstance);
            _activationWindow.ActivateRequested += delegate { ShowMain(); };

            InitializeHotkey(silentStart);

            _scheduler.Start();
            AppLog.Info("App.Started");

            if (!silentStart)
            {
                ShowMain();
            }
        }

        private void InitializeHotkey(bool silentStart)
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += delegate { ShowQuickAdd(); };

            if (!_hotkey.Register())
            {
                AppLog.Warn("Hotkey.RegisterFailed", "Ctrl+Alt+N");
                if (!silentStart)
                {
                    UiMessageBox.Info(
                        null,
                        "快捷键 Ctrl+Alt+N 被其他程序占用，快速添加暂时不可用。");
                }
            }
        }

        private void ShowQuickAdd()
        {
            if (_quickAdd != null && !_quickAdd.IsDisposed)
            {
                _quickAdd.FocusInput();
                return;
            }

            _quickAdd = new QuickAddForm(OnQuickAddSubmit);
            _quickAdd.FormClosed += delegate { _quickAdd = null; };
            _quickAdd.Show();
            _quickAdd.FocusInput();
        }

        private void OnQuickAddSubmit(QuickParseResult parsed)
        {
            if (parsed == null)
            {
                return;
            }

            TodoTask task = new TodoTask();
            task.Title = string.IsNullOrWhiteSpace(parsed.Title) ? "未命名任务" : parsed.Title;
            task.Group = parsed.Group ?? string.Empty;
            task.DueUtc = DateTime.SpecifyKind(parsed.DueLocal, DateTimeKind.Local).ToUniversalTime();
            task.Style = ReminderStyle.Card;
            task.Enabled = true;

            _tasks.Add(task);
            SaveTasks();

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SetTasks(_tasks);
            }

            AppLog.Info("Task.QuickAdded");
        }

        private void ShowMain()
        {
            MainForm form = EnsureMainForm();
            form.SetTasks(_tasks);
            form.ShowMain();
        }

        private MainForm EnsureMainForm()
        {
            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                return _mainForm;
            }

            _mainForm = new MainForm();
            _mainForm.NewTaskRequested += delegate { CreateTask(); };
            _mainForm.EditRequested += OnEditRequested;
            _mainForm.DeleteRequested += OnDeleteRequested;
            _mainForm.SettingsRequested += OnSettingsRequested;
            _mainForm.ThemeToggleRequested += OnThemeToggleRequested;
            _mainForm.PinToggleRequested += OnPinToggleRequested;
            _mainForm.TaskToggleRequested += OnTaskToggleRequested;
            _mainForm.CleanCompletedRequested += OnCleanCompletedRequested;
            _mainForm.AutoStartChanged += OnAutoStartChanged;
            _mainForm.PauseChanged += OnMainPauseChanged;
            _mainForm.SetTasks(_tasks);

            _syncing = true;
            try
            {
                _mainForm.AutoStartChecked = AutoStart.IsEnabled();
                _mainForm.PauseChecked = _settings.Paused;
            }
            finally
            {
                _syncing = false;
            }

            return _mainForm;
        }

        private void CreateTask()
        {
            MainForm owner = EnsureMainForm();
            using (TaskEditForm dialog = new TaskEditForm(null, CollectGroups()))
            {
                owner.ShowMain();
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Result == null)
                {
                    return;
                }

                _tasks.Add(dialog.Result);
            }

            SaveTasks();
            owner.SetTasks(_tasks);
        }

        private List<string> CollectGroups()
        {
            List<string> groups = new List<string>();
            foreach (TodoTask task in _tasks)
            {
                if (!string.IsNullOrWhiteSpace(task.Group) && !groups.Contains(task.Group))
                {
                    groups.Add(task.Group);
                }
            }

            groups.Sort(StringComparer.CurrentCulture);
            return groups;
        }

        private void OnThemeToggleRequested(object sender, EventArgs e)
        {
            _settings.DarkMode = !_settings.DarkMode;
            SaveSettings();

            Theme.ApplyTheme(_settings.DarkMode);

            // 深色切换涉及系统控件配色，直接重建主窗最稳妥，重建后恢复视图与窗口位置。
            TaskView view = TaskView.Today;
            string group = null;
            Rectangle bounds = Rectangle.Empty;
            FormWindowState state = FormWindowState.Normal;
            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                view = _mainForm.CurrentView;
                group = _mainForm.CurrentGroup;
                state = _mainForm.WindowState;
                bounds = state == FormWindowState.Maximized ? _mainForm.RestoreBounds : _mainForm.Bounds;

                _mainForm.AllowClose = true;
                _mainForm.Close();
                _mainForm.Dispose();
                _mainForm = null;
            }

            ShowMain();
            if (!bounds.IsEmpty)
            {
                _mainForm.StartPosition = FormStartPosition.Manual;
                _mainForm.Bounds = bounds;
                _mainForm.WindowState = state;
            }

            _mainForm.CurrentView = view;
            _mainForm.CurrentGroup = group;
        }

        private void OnSettingsRequested(object sender, EventArgs e)
        {
            MainForm owner = EnsureMainForm();
            using (SettingsForm dialog = new SettingsForm(_settings.OverlayDim))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return;
                }

                _settings.OverlayDim = dialog.OverlayDim;
                SaveSettings();
            }
        }

        private void OnPinToggleRequested(object sender, TaskEventArgs e)
        {
            if (e.Task == null)
            {
                return;
            }

            e.Task.Pinned = !e.Task.Pinned;
            SaveTasks();

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SetTasks(_tasks);
            }
        }

        private void OnTaskToggleRequested(object sender, TaskEventArgs e)
        {
            TodoTask task = e.Task;
            if (task == null || task.State == TaskState.Done)
            {
                return;
            }

            CompleteTask(task);
            SaveTasks();
            RefreshMain();
        }

        /// <summary>把任务标记完成：一次性任务结案，重复任务推进到下一次。</summary>
        private void CompleteTask(TodoTask task)
        {
            DateTime now = DateTime.UtcNow;

            if (task.Recurrence == RecurrenceKind.None)
            {
                task.State = TaskState.Done;
                task.CompletedUtc = now;
                task.SnoozeUntilUtc = null;
            }
            else
            {
                task.DueUtc = RecurrenceCalculator.NextAfter(
                    task.DueUtc,
                    task.Recurrence,
                    task.WeekdaysMask,
                    task.RecurrenceInterval,
                    now);
                task.State = TaskState.Pending;
                task.SnoozeUntilUtc = null;
                task.LastFiredUtc = null;
                task.CompletedUtc = null;
            }

            if (_scheduler != null)
            {
                _scheduler.ResetFiringState(task.Id);
            }

            // 完成/滚动后队列里的旧触发作废：重复任务否则会提前弹出下一次提醒。
            PurgeQueued(task.Id);
            CloseReminder(task.Id);
        }

        private void RefreshMain()
        {
            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SetTasks(_tasks);
            }
        }

        private void OnCleanCompletedRequested(object sender, EventArgs e)
        {
            int completed = 0;
            foreach (TodoTask task in _tasks)
            {
                if (task.State == TaskState.Done)
                {
                    completed++;
                }
            }

            if (completed == 0)
            {
                UiMessageBox.Info(_mainForm, "当前没有已完成的任务。");
                return;
            }

            bool confirm = UiMessageBox.Confirm(
                _mainForm,
                "确定删除 " + completed + " 条已完成任务吗？此操作不可恢复。",
                "删除",
                true);

            if (!confirm)
            {
                return;
            }

            _tasks.RemoveAll(delegate(TodoTask task) { return task.State == TaskState.Done; });
            SaveTasks();

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SetTasks(_tasks);
            }
        }

        private void OnEditRequested(object sender, TaskEventArgs e)
        {
            if (e.Task == null)
            {
                return;
            }

            MainForm owner = EnsureMainForm();
            using (TaskEditForm dialog = new TaskEditForm(e.Task, CollectGroups()))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Result == null)
                {
                    return;
                }

                CloseReminder(e.Task.Id);

                int index = _tasks.FindIndex(delegate(TodoTask item) { return item.Id == dialog.Result.Id; });
                if (index >= 0)
                {
                    _tasks[index] = dialog.Result;
                }
                else
                {
                    _tasks.Add(dialog.Result);
                }

                // 时间规则被重排时 LastFiredUtc 被清空，队列里残留的旧触发作废。
                if (dialog.Result.LastFiredUtc == null)
                {
                    PurgeQueued(dialog.Result.Id);
                }
            }

            SaveTasks();
            owner.SetTasks(_tasks);
        }

        private void OnDeleteRequested(object sender, TaskEventArgs e)
        {
            if (e.Task == null)
            {
                return;
            }

            MainForm owner = EnsureMainForm();
            bool confirm = UiMessageBox.Confirm(
                owner,
                "确定删除「" + e.Task.Title + "」吗？此操作不可恢复。",
                "删除",
                true);

            if (!confirm)
            {
                return;
            }

            CloseReminder(e.Task.Id);
            _tasks.RemoveAll(delegate(TodoTask item) { return item.Id == e.Task.Id; });
            SaveTasks();
            owner.SetTasks(_tasks);
        }

        private void OnAutoStartChanged(object sender, EventArgs e)
        {
            if (_syncing || _mainForm == null)
            {
                return;
            }

            bool enabled = _mainForm.AutoStartChecked;
            if (!AutoStart.SetEnabled(enabled))
            {
                UiMessageBox.Warn(_mainForm, "写入开机启动项失败，请稍后再试。");
            }

            _settings.AutoStart = enabled;
            SaveSettings();
        }

        private void OnMainPauseChanged(object sender, EventArgs e)
        {
            if (_syncing || _mainForm == null)
            {
                return;
            }

            ApplyPause(_mainForm.PauseChecked, true);
        }

        private void OnTrayPauseChanged(object sender, EventArgs e)
        {
            ApplyPause(_tray.Paused, true);
        }

        private void ApplyPause(bool paused, bool syncMain)
        {
            _settings.Paused = paused;
            if (_scheduler != null)
            {
                _scheduler.Paused = paused;
            }

            if (paused)
            {
                // 暂停后不应再弹出排队中的提醒，但把触发记录退掉，
                // 解除暂停后仍能补提醒，不会在内存里被记成「已提醒」而静默丢掉。
                foreach (TodoTask task in _overlayQueue)
                {
                    task.LastFiredUtc = null;
                }

                foreach (TodoTask task in _cardQueue)
                {
                    task.LastFiredUtc = null;
                }

                _cardQueue.Clear();
                _overlayQueue.Clear();
            }

            if (_tray != null)
            {
                _tray.Paused = paused;
            }

            if (syncMain && _mainForm != null && !_mainForm.IsDisposed)
            {
                _syncing = true;
                try
                {
                    _mainForm.PauseChecked = paused;
                }
                finally
                {
                    _syncing = false;
                }
            }

            SaveSettings();
        }

        private void OnTaskDue(object sender, TaskDueEventArgs e)
        {
            TodoTask task = e.Task;
            if (task == null || task.Id == Guid.Empty || _settings.Paused)
            {
                return;
            }

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.SetTasks(_tasks);
            }

            if (_openReminders.ContainsKey(task.Id))
            {
                return;
            }

            if (task.Style == ReminderStyle.FullScreen)
            {
                // 全屏强提醒同一时刻只呈现一个，其余排队，避免遮罩层层叠加。
                if (_activeOverlay != null)
                {
                    _overlayQueue.Enqueue(task);
                    AppLog.Info("Reminder.OverlayQueued");
                    return;
                }

                ShowOverlay(task, e.IsLate);
            }
            else
            {
                // 卡片同样串行展示，不堆叠遮挡。
                if (_activeOverlay != null || _activeCard != null)
                {
                    _cardQueue.Enqueue(task);
                    AppLog.Info("Reminder.CardQueued");
                    return;
                }

                ShowCard(task, e.IsLate);
            }

            AppLog.Info("Reminder.Shown");
        }

        private void ShowOverlay(TodoTask task, bool isLate)
        {
            OverlayForm overlay = new OverlayForm(task, isLate, _settings.OverlayDim, delegate(ReminderResult result)
            {
                HandleReminderResult(task, result);
            });

            overlay.FormClosed += delegate
            {
                RemoveReminder(task.Id, overlay);
                if (ReferenceEquals(_activeOverlay, overlay))
                {
                    _activeOverlay = null;
                }

                ShowNextReminder();
            };

            _activeOverlay = overlay;
            _openReminders[task.Id] = overlay;
            overlay.ShowOverlay();
            // 真正弹出后再落盘：此前退出/崩溃时不会把 LastFiredUtc 存下去，重启仍能补提醒。
            SaveTasks();
        }

        private void ShowCard(TodoTask task, bool isLate)
        {
            ReminderCardForm card = new ReminderCardForm(task, isLate, delegate(ReminderResult result)
            {
                HandleReminderResult(task, result);
            });

            card.FormClosed += delegate
            {
                RemoveReminder(task.Id, card);
                if (ReferenceEquals(_activeCard, card))
                {
                    _activeCard = null;
                }

                ShowNextReminder();
            };

            _activeCard = card;
            _openReminders[task.Id] = card;
            card.ShowCard();
            SaveTasks();
        }

        private void ShowNextReminder()
        {
            if (_exiting || _settings.Paused)
            {
                return;
            }

            if (_activeOverlay == null)
            {
                TodoTask overlayTask;
                if (TryDequeue(_overlayQueue, out overlayTask))
                {
                    ShowOverlay(overlayTask, IsLate(overlayTask));
                    return;
                }
            }

            if (_activeOverlay == null && _activeCard == null)
            {
                TodoTask cardTask;
                if (TryDequeue(_cardQueue, out cardTask))
                {
                    ShowCard(cardTask, IsLate(cardTask));
                }
            }
        }

        private bool TryDequeue(Queue<TodoTask> queue, out TodoTask task)
        {
            while (queue.Count > 0)
            {
                TodoTask queued = queue.Dequeue();
                if (queued == null)
                {
                    continue;
                }

                // 队列里可能是编辑前的旧实例，按 Id 找到当前实例再弹，避免编辑后提醒被丢或显示旧数据。
                TodoTask current = FindById(queued.Id);
                if (current != null && current.IsActive)
                {
                    task = current;
                    return true;
                }
            }

            task = null;
            return false;
        }

        private TodoTask FindById(Guid id)
        {
            foreach (TodoTask item in _tasks)
            {
                if (item.Id == id)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>任务被完成/改期后，把队列里残留的旧触发作废，避免提前弹出下一次提醒。</summary>
        private void PurgeQueued(Guid taskId)
        {
            PurgeQueued(_overlayQueue, taskId);
            PurgeQueued(_cardQueue, taskId);
        }

        private static void PurgeQueued(Queue<TodoTask> queue, Guid taskId)
        {
            if (queue.Count == 0)
            {
                return;
            }

            List<TodoTask> keep = new List<TodoTask>();
            while (queue.Count > 0)
            {
                TodoTask item = queue.Dequeue();
                if (item != null && item.Id != taskId)
                {
                    keep.Add(item);
                }
            }

            foreach (TodoTask item in keep)
            {
                queue.Enqueue(item);
            }
        }

        private static bool IsLate(TodoTask task)
        {
            return task.DueUtc < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(1));
        }

        private void RemoveReminder(Guid taskId, Form form)
        {
            Form existing;
            if (_openReminders.TryGetValue(taskId, out existing) && ReferenceEquals(existing, form))
            {
                _openReminders.Remove(taskId);
            }
        }

        private void CloseReminder(Guid taskId)
        {
            Form form;
            if (_openReminders.TryGetValue(taskId, out form))
            {
                _openReminders.Remove(taskId);
                CloseReminderForm(form);
            }
        }

        /// <summary>内部关闭提醒窗口，绕过「用户关窗=延后」的误操作语义。</summary>
        private static void CloseReminderForm(Form form)
        {
            OverlayForm overlay = form as OverlayForm;
            if (overlay != null)
            {
                overlay.ForceClose();
                return;
            }

            ReminderCardForm card = form as ReminderCardForm;
            if (card != null)
            {
                card.ForceClose();
                return;
            }

            form.Close();
        }

        private void HandleReminderResult(TodoTask task, ReminderResult result)
        {
            if (task == null || result == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;

            switch (result.Kind)
            {
                case ReminderActionKind.Complete:
                    CompleteTask(task);
                    break;

                case ReminderActionKind.Snooze:
                    task.SnoozeUntilUtc = now.AddMinutes(result.Minutes);
                    task.LastFiredUtc = now;
                    task.State = TaskState.Snoozed;
                    if (_scheduler != null)
                    {
                        _scheduler.ResetFiringState(task.Id);
                    }

                    break;
            }

            SaveTasks();
            RefreshMain();
        }

        private void SaveTasks()
        {
            // 排队中还没弹出的提醒不落 LastFiredUtc：临时清空、存完恢复，
            // 这样退出/崩溃/其它改动触发的落盘都不会把它记成「已提醒」，重启后仍能补上。
            List<TodoTask> queued = new List<TodoTask>();
            List<DateTime?> saved = new List<DateTime?>();
            foreach (TodoTask task in _overlayQueue)
            {
                queued.Add(task);
                saved.Add(task.LastFiredUtc);
                task.LastFiredUtc = null;
            }

            foreach (TodoTask task in _cardQueue)
            {
                queued.Add(task);
                saved.Add(task.LastFiredUtc);
                task.LastFiredUtc = null;
            }

            try
            {
                _store.Save(_tasks);
            }
            finally
            {
                for (int i = 0; i < queued.Count; i++)
                {
                    queued[i].LastFiredUtc = saved[i];
                }
            }
        }

        private void SaveSettings()
        {
            _settingsStore.Save(_settings);
        }

        private void ExitApplication()
        {
            _exiting = true;
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            bool userRequested = _exiting;
            _exiting = true;
            AppLog.Info(userRequested ? "App.Exit.User" : "App.Exit.System");

            try
            {
                SaveTasks();
                SaveSettings();
            }
            catch (Exception ex)
            {
                AppLog.Error("App.Exit.SaveFailed", ex);
            }

            if (_scheduler != null)
            {
                _scheduler.Dispose();
                _scheduler = null;
            }

            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }

            if (_activationWindow != null)
            {
                _activationWindow.Dispose();
                _activationWindow = null;
            }

            _overlayQueue.Clear();
            _cardQueue.Clear();
            _activeOverlay = null;
            _activeCard = null;

            if (_hotkey != null)
            {
                _hotkey.Dispose();
                _hotkey = null;
            }

            if (_quickAdd != null && !_quickAdd.IsDisposed)
            {
                _quickAdd.Close();
                _quickAdd = null;
            }

            List<Form> reminders = new List<Form>(_openReminders.Values);
            _openReminders.Clear();
            foreach (Form form in reminders)
            {
                if (form.IsDisposed)
                {
                    continue;
                }

                CloseReminderForm(form);
            }

            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.AllowClose = true;
                _mainForm.Close();
                _mainForm.Dispose();
                _mainForm = null;
            }

            base.ExitThreadCore();
        }
    }
}
