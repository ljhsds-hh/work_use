using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    internal sealed class TaskEventArgs : EventArgs
    {
        public TaskEventArgs(TodoTask task)
        {
            Task = task;
        }

        public TodoTask Task { get; private set; }
    }

    internal enum TaskView
    {
        All = 0,
        Today = 1,
        Overdue = 2,
        Done = 3
    }

    /// <summary>主窗口：左侧导航 + 右侧自绘任务列表。</summary>
    internal sealed class MainForm : Form
    {
        private const string DefaultGroup = "默认";

        private readonly SideNav _nav;
        private readonly TaskListView _list;
        private readonly EmptyStateView _empty;
        private readonly UiSearchBox _search;
        private readonly UiButton _create;
        private readonly UiSwitchItem _pauseSwitch;
        private readonly UiSwitchItem _autoStartSwitch;
        private readonly UiButton _cleanButton;
        private readonly Label _titleLabel;
        private readonly Label _countLabel;

        private List<TodoTask> _allTasks = new List<TodoTask>();
        private TaskView _view = TaskView.Today;
        private string _group;
        private bool _allowClose;
        private bool _refreshing;

        public MainForm()
        {
            Text = "小叮";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.FontBody;
            Icon = TrayIconHost.CreateAppIcon();
            ClientSize = new Size(Theme.S(900), Theme.S(620));
            MinimumSize = new Size(Theme.S(916), Theme.S(660));

            _nav = new SideNav();
            _nav.SetBounds(0, 0, Theme.S(204), ClientSize.Height);
            _nav.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            _nav.ViewRequested += OnNavViewRequested;
            _nav.GroupRequested += OnNavGroupRequested;
            _nav.SettingsRequested += delegate
            {
                EventHandler handler = SettingsRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _nav.ThemeToggleRequested += delegate
            {
                EventHandler handler = ThemeToggleRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            Controls.Add(_nav);

            _titleLabel = new Label();
            _titleLabel.AutoSize = false;
            _titleLabel.Font = Theme.DisplayFont(15f, FontStyle.Bold);
            _titleLabel.ForeColor = Theme.Text;
            _titleLabel.BackColor = Color.Transparent;
            _titleLabel.SetBounds(Theme.S(226), Theme.S(14), Theme.S(180), Theme.S(34));
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_titleLabel);

            _countLabel = new Label();
            _countLabel.AutoSize = false;
            _countLabel.Font = Theme.FontMeta;
            _countLabel.ForeColor = Theme.TextFaint;
            _countLabel.BackColor = Color.Transparent;
            _countLabel.TextAlign = ContentAlignment.MiddleLeft;
            _countLabel.SetBounds(Theme.S(290), Theme.S(22), Theme.S(160), Theme.S(22));
            Controls.Add(_countLabel);
            _countLabel.BringToFront();

            _create = new UiButton();
            _create.Primary = true;
            _create.Text = "＋ 新建";
            _create.SetBounds(ClientSize.Width - Theme.S(124), Theme.S(18), Theme.S(100), Theme.S(34));
            _create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _create.Click += delegate { RaiseNewTask(); };
            Controls.Add(_create);

            _search = new UiSearchBox();
            _search.SetBounds(ClientSize.Width - Theme.S(328), Theme.S(19), Theme.S(192), Theme.S(32));
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _search.TextChangedEvent += delegate { RefreshAll(); };
            Controls.Add(_search);

            _list = new TaskListView();
            _list.SetBounds(Theme.S(204), Theme.S(62), ClientSize.Width - Theme.S(204), ClientSize.Height - Theme.S(108));
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.Paint += delegate(object sender, PaintEventArgs e)
            {
                // 头部与列表之间一条发丝线，分出层次。
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, _list.Width, 0);
                }
            };
            _list.TaskActivated += delegate(object sender, TaskEventArgs e) { RaiseEdit(e.Task); };
            _list.TaskDeleteRequested += delegate(object sender, TaskEventArgs e) { RaiseDelete(e.Task); };
            _list.TaskToggleRequested += delegate(object sender, TaskEventArgs e) { RaiseToggle(e.Task); };
            _list.TaskMenuRequested += OnTaskMenuRequested;
            Controls.Add(_list);

            _empty = new EmptyStateView();
            _empty.SetBounds(Theme.S(204), Theme.S(62), ClientSize.Width - Theme.S(204), ClientSize.Height - Theme.S(108));
            _empty.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _empty.Visible = false;
            Controls.Add(_empty);

            Panel foot = new Panel();
            foot.BackColor = Theme.SurfaceMuted;
            foot.SetBounds(Theme.S(204), ClientSize.Height - Theme.S(46), ClientSize.Width - Theme.S(204), Theme.S(46));
            foot.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            foot.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0);
                }
            };
            Controls.Add(foot);

            _pauseSwitch = new UiSwitchItem();
            _pauseSwitch.Text = "暂停全部提醒";
            _pauseSwitch.SetBounds(Theme.S(22), Theme.S(11), Theme.S(136), Theme.S(24));
            _pauseSwitch.CheckedChangedEvent += delegate
            {
                EventHandler handler = PauseChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            foot.Controls.Add(_pauseSwitch);

            _autoStartSwitch = new UiSwitchItem();
            _autoStartSwitch.Text = "开机自启";
            _autoStartSwitch.SetBounds(Theme.S(176), Theme.S(11), Theme.S(112), Theme.S(24));
            _autoStartSwitch.CheckedChangedEvent += delegate
            {
                EventHandler handler = AutoStartChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            foot.Controls.Add(_autoStartSwitch);

            _cleanButton = new UiButton();
            _cleanButton.Link = true;
            _cleanButton.Text = "清理已完成";
            _cleanButton.SetBounds(foot.Width - Theme.S(124), Theme.S(8), Theme.S(100), Theme.S(30));
            _cleanButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _cleanButton.Click += delegate
            {
                EventHandler handler = CleanCompletedRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            foot.Controls.Add(_cleanButton);

            RefreshAll();
        }

        public event EventHandler NewTaskRequested;

        public event EventHandler<TaskEventArgs> EditRequested;

        public event EventHandler<TaskEventArgs> DeleteRequested;

        public event EventHandler SettingsRequested;

        public event EventHandler ThemeToggleRequested;

        public event EventHandler<TaskEventArgs> PinToggleRequested;

        public event EventHandler<TaskEventArgs> TaskToggleRequested;

        public event EventHandler CleanCompletedRequested;

        public event EventHandler AutoStartChanged;

        public event EventHandler PauseChanged;

        public bool AllowClose
        {
            get { return _allowClose; }
            set { _allowClose = value; }
        }

        public bool AutoStartChecked
        {
            get { return _autoStartSwitch.Checked; }
            set { _autoStartSwitch.SetCheckedSilently(value); }
        }

        public bool PauseChecked
        {
            get { return _pauseSwitch.Checked; }
            set { _pauseSwitch.SetCheckedSilently(value); }
        }

        public void ShowMain()
        {
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        public void SetTasks(List<TodoTask> tasks)
        {
            _allTasks = tasks ?? new List<TodoTask>();
            RefreshAll();
        }

        public TodoTask SelectedTask
        {
            get { return _list.SelectedTask; }
        }

        /// <summary>当前视图，供主题切换重建窗口后恢复。</summary>
        public TaskView CurrentView
        {
            get { return _view; }
            set
            {
                if (_view != value)
                {
                    _view = value;
                    RefreshAll();
                }
            }
        }

        public string CurrentGroup
        {
            get { return _group; }
            set
            {
                if (!string.Equals(_group, value, StringComparison.CurrentCulture))
                {
                    _group = value;
                    RefreshAll();
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _search.SetCue("搜索任务");
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 运行时切换主题后窗口是重建的，句柄刚建好时设置偶尔不生效，显示后再补一次。
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        private void OnNavViewRequested(object sender, NavViewEventArgs e)
        {
            if (_view == e.View)
            {
                return;
            }

            _view = e.View;
            RefreshAll();
        }

        private void OnNavGroupRequested(object sender, NavGroupEventArgs e)
        {
            _group = string.Equals(_group, e.Group, StringComparison.CurrentCulture) ? null : e.Group;
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_refreshing || _list == null)
            {
                return;
            }

            _refreshing = true;
            try
            {
                string keyword = _search.Query;
                DateTime now = DateTime.UtcNow;
                DateTime todayLocal = DateTime.Today;

                List<TodoTask> visible = new List<TodoTask>();
                foreach (TodoTask task in _allTasks)
                {
                    if (!MatchesView(task, _view, now, todayLocal))
                    {
                        continue;
                    }

                    if (_group != null && !string.Equals(NormalizeGroup(task.Group), _group, StringComparison.CurrentCulture))
                    {
                        continue;
                    }

                    if (keyword.Length > 0 && !MatchesKeyword(task, keyword))
                    {
                        continue;
                    }

                    visible.Add(task);
                }

                List<string> groupOrder = BuildGroupOrder();
                Dictionary<string, int> ranks = new Dictionary<string, int>();
                for (int i = 0; i < groupOrder.Count; i++)
                {
                    ranks[groupOrder[i]] = i;
                }

                visible.Sort(delegate(TodoTask left, TodoTask right)
                {
                    if (left.Pinned != right.Pinned)
                    {
                        return left.Pinned ? -1 : 1;
                    }

                    int leftRank = RankOf(ranks, left);
                    int rightRank = RankOf(ranks, right);
                    if (leftRank != rightRank)
                    {
                        return leftRank.CompareTo(rightRank);
                    }

                    bool leftDone = left.State == TaskState.Done;
                    bool rightDone = right.State == TaskState.Done;
                    if (leftDone != rightDone)
                    {
                        return leftDone ? 1 : -1;
                    }

                    return left.DueUtc.CompareTo(right.DueUtc);
                });

                List<TaskGroup> groups = new List<TaskGroup>();
                foreach (string name in groupOrder)
                {
                    List<TodoTask> items = new List<TodoTask>();
                    foreach (TodoTask task in visible)
                    {
                        if (string.Equals(NormalizeGroup(task.Group), name, StringComparison.CurrentCulture))
                        {
                            items.Add(task);
                        }
                    }

                    if (items.Count > 0)
                    {
                        TaskGroup group = new TaskGroup(name);
                        group.Tasks.AddRange(items);
                        groups.Add(group);
                    }
                }

                _list.SetGroups(groups);
                _list.Visible = visible.Count > 0;
                _empty.Visible = visible.Count == 0;
                UpdateEmptyText(visible.Count, keyword);

                int[] counts = BuildViewCounts(now, todayLocal);
                List<KeyValuePair<string, int>> groupCounts = BuildGroupCounts(now, todayLocal);
                _nav.SetData(_view, _group, counts, groupCounts, Theme.IsDark);

                _titleLabel.Text = ViewTitle(_view);
                int titleWidth = TextRenderer.MeasureText(_titleLabel.Text, _titleLabel.Font).Width;
                _titleLabel.Width = titleWidth + Theme.S(4);
                _countLabel.Left = _titleLabel.Right + Theme.S(8);
                _countLabel.Text = visible.Count + " 项待办";
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void UpdateEmptyText(int visibleCount, string keyword)
        {
            if (_allTasks.Count == 0)
            {
                _empty.Title = "今天还没有安排";
                _empty.Description = "按快捷键随手记一条，剩下的交给小叮。";
            }
            else if (visibleCount == 0 && keyword.Length > 0)
            {
                _empty.Title = "没有匹配的任务";
                _empty.Description = "换个关键词试试，或清空搜索。";
            }
            else if (visibleCount == 0 && _group != null)
            {
                _empty.Title = "「" + _group + "」暂时没有任务";
                _empty.Description = "切换到其它分组看看。";
            }
            else
            {
                _empty.Title = "这个视图是空的";
                _empty.Description = "去「全部」看看，或新建一条任务。";
            }

            _empty.Invalidate();
        }

        private int[] BuildViewCounts(DateTime now, DateTime todayLocal)
        {
            int[] counts = new int[4];
            foreach (TodoTask task in _allTasks)
            {
                if (MatchesView(task, TaskView.Today, now, todayLocal))
                {
                    counts[0]++;
                }

                if (MatchesView(task, TaskView.Overdue, now, todayLocal))
                {
                    counts[1]++;
                }

                counts[2]++;

                if (MatchesView(task, TaskView.Done, now, todayLocal))
                {
                    counts[3]++;
                }
            }

            return counts;
        }

        private List<KeyValuePair<string, int>> BuildGroupCounts(DateTime now, DateTime todayLocal)
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();
            foreach (string name in BuildGroupOrder())
            {
                int count = 0;
                foreach (TodoTask task in _allTasks)
                {
                    if (string.Equals(NormalizeGroup(task.Group), name, StringComparison.CurrentCulture)
                        && MatchesView(task, _view, now, todayLocal))
                    {
                        count++;
                    }
                }

                if (count > 0)
                {
                    result.Add(new KeyValuePair<string, int>(name, count));
                }
            }

            return result;
        }

        private void OnTaskMenuRequested(object sender, TaskEventArgs e)
        {
            TodoTask task = e.Task;
            if (task == null)
            {
                return;
            }

            UiMenu menu = new UiMenu();
            menu.Add(task.Pinned ? "取消置顶" : "置顶", delegate
            {
                EventHandler<TaskEventArgs> handler = PinToggleRequested;
                if (handler != null)
                {
                    handler(this, new TaskEventArgs(task));
                }
            });
            menu.Add("编辑", delegate { RaiseEdit(task); });
            menu.Add("删除", delegate { RaiseDelete(task); }, true);

            menu.ShowAt(Cursor.Position);
        }

        private void RaiseNewTask()
        {
            EventHandler handler = NewTaskRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseEdit(TodoTask task)
        {
            if (task == null)
            {
                return;
            }

            EventHandler<TaskEventArgs> handler = EditRequested;
            if (handler != null)
            {
                handler(this, new TaskEventArgs(task));
            }
        }

        private void RaiseDelete(TodoTask task)
        {
            if (task == null)
            {
                return;
            }

            EventHandler<TaskEventArgs> handler = DeleteRequested;
            if (handler != null)
            {
                handler(this, new TaskEventArgs(task));
            }
        }

        private void RaiseToggle(TodoTask task)
        {
            if (task == null)
            {
                return;
            }

            EventHandler<TaskEventArgs> handler = TaskToggleRequested;
            if (handler != null)
            {
                handler(this, new TaskEventArgs(task));
            }
        }

        private List<string> BuildGroupOrder()
        {
            List<string> groups = new List<string>();
            bool hasDefault = false;

            foreach (TodoTask task in _allTasks)
            {
                if (string.IsNullOrWhiteSpace(task.Group))
                {
                    hasDefault = true;
                    continue;
                }

                if (!groups.Contains(task.Group))
                {
                    groups.Add(task.Group);
                }
            }

            if (hasDefault)
            {
                groups.Add(DefaultGroup);
            }

            return groups;
        }

        private static string ViewTitle(TaskView view)
        {
            switch (view)
            {
                case TaskView.Today:
                    return "今天";
                case TaskView.Overdue:
                    return "逾期";
                case TaskView.Done:
                    return "已完成";
                default:
                    return "全部";
            }
        }

        private static bool MatchesView(TodoTask task, TaskView view, DateTime nowUtc, DateTime todayLocal)
        {
            switch (view)
            {
                case TaskView.Today:
                    return task.State != TaskState.Done
                        && DateTime.SpecifyKind(task.DueUtc, DateTimeKind.Utc).ToLocalTime().Date == todayLocal;

                case TaskView.Overdue:
                    return task.State != TaskState.Done && task.DueUtc < nowUtc;

                case TaskView.Done:
                    return task.State == TaskState.Done;

                default:
                    return true;
            }
        }

        private static bool MatchesKeyword(TodoTask task, string keyword)
        {
            if (!string.IsNullOrEmpty(task.Title)
                && task.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return !string.IsNullOrEmpty(task.Note)
                && task.Note.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int RankOf(Dictionary<string, int> ranks, TodoTask task)
        {
            int rank;
            return ranks.TryGetValue(NormalizeGroup(task.Group), out rank) ? rank : int.MaxValue;
        }

        private static string NormalizeGroup(string group)
        {
            return string.IsNullOrWhiteSpace(group) ? DefaultGroup : group;
        }
    }
}
