using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>
    /// 新建/编辑任务对话框：内容、时间、重复、提醒方式四段式，全部使用自绘控件。
    /// 底部操作栏固定可见，内容区仅在矮屏/高 DPI 下滚动，避免按钮被推出视野。
    /// </summary>
    internal sealed class TaskEditForm : Form
    {
        private const int PadLeft = 26;
        private const int ContentWidth = 508;
        private const int FormWidth = 560;

        private readonly TodoTask _original;
        private readonly UiTextField _titleField;
        private readonly UiTextField _noteField;
        private readonly UiTextField _groupField;
        private readonly UiDateField _dateField;
        private readonly UiTimeField _timeField;
        private readonly UiTextField _remindField;
        private readonly UiUnitBox _remindUnit;
        private readonly UiChipGroup _recurrenceChips;
        private readonly UiChipGroup _weekdayChips;
        private readonly UiChipGroup _styleChips;
        private readonly UiSwitchItem _enabledSwitch;
        private readonly UiTextField _intervalField;
        private readonly Label _intervalUnit;

        private readonly Label _secContent;
        private readonly Label _secTime;
        private readonly Label _secRecurrence;
        private readonly Label _secStyle;
        private readonly Label _lblTitle;
        private readonly Label _lblNote;
        private readonly Label _lblGroup;
        private readonly Label _lblDate;
        private readonly Label _lblTime;
        private readonly Label _lblRemind;
        private readonly Label _remindHint;

        private readonly Panel _content;
        private readonly Panel _footer;
        private readonly Label _footerHint;
        private readonly UiButton _cancel;
        private readonly UiButton _save;

        public TaskEditForm(TodoTask existing, IEnumerable<string> groups)
        {
            _original = existing;
            bool isNew = existing == null;
            TodoTask source = existing ?? new TodoTask();
            DateTime dueLocal = DateTime.SpecifyKind(source.DueUtc, DateTimeKind.Utc).ToLocalTime();
            if (isNew || source.DueUtc == default(DateTime))
            {
                dueLocal = RoundUpToNextHalfHour(DateTime.Now);
            }

            Text = isNew ? "新建任务" : "编辑任务";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.FontBody;

            // 固定底栏先建，内容区占满剩余空间并独立滚动。
            _footer = new Panel();
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = Theme.S(64);
            _footer.BackColor = Theme.SurfaceMuted;
            _footer.Paint += OnFooterPaint;
            Controls.Add(_footer);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.AutoScroll = true;
            // 内容永远按窗宽排布，不需要横向滚动；显式关掉避免出现底部滚动条遮挡。
            _content.HorizontalScroll.Enabled = false;
            _content.HorizontalScroll.Visible = false;
            _content.BackColor = Theme.Surface;
            Controls.Add(_content);

            _secContent = AddSectionLabel("内容");
            _lblTitle = AddFieldLabel("任务内容");
            _titleField = new UiTextField();
            _titleField.Text = source.Title ?? string.Empty;
            _titleField.MaxLength = 120;
            AddContent(_titleField);

            _lblNote = AddFieldLabel("备注");
            _noteField = new UiTextField();
            _noteField.Multiline = true;
            _noteField.Text = source.Note ?? string.Empty;
            _noteField.MaxLength = 500;
            AddContent(_noteField);

            _lblGroup = AddFieldLabel("分组");
            _groupField = new UiTextField();
            _groupField.Text = source.Group ?? string.Empty;
            _groupField.MaxLength = 20;
            AddContent(_groupField);

            _secTime = AddSectionLabel("时间");
            _lblDate = AddFieldLabel("日期");
            _dateField = new UiDateField();
            _dateField.Value = dueLocal.Date;
            AddContent(_dateField);

            _lblTime = AddFieldLabel("时间");
            _timeField = new UiTimeField();
            _timeField.Value = dueLocal.TimeOfDay;
            AddContent(_timeField);

            _lblRemind = AddFieldLabel("提前提醒");
            _remindHint = AddHintLabel("0 表示到点提醒");
            _remindField = new UiTextField();
            _remindField.Text = Math.Max(0, Math.Min(720, source.RemindBeforeMinutes)).ToString();
            AddContent(_remindField);

            _remindUnit = new UiUnitBox();
            _remindUnit.Text = "分钟";
            AddContent(_remindUnit);

            _secRecurrence = AddSectionLabel("重复");
            _recurrenceChips = new UiChipGroup();
            _recurrenceChips.SetItems(new string[] { "不重复", "每天", "每周", "工作日", "自定义星期", "每月某日", "每 N 天" });
            _recurrenceChips.SelectedIndex = Math.Max(0, (int)source.Recurrence);
            AddContent(_recurrenceChips);

            _weekdayChips = new UiChipGroup();
            _weekdayChips.MultiSelect = true;
            _weekdayChips.SetItems(new string[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
            bool[] masks = new bool[7];
            for (int i = 0; i < 7; i++)
            {
                masks[i] = (source.WeekdaysMask & (1 << i)) != 0;
            }

            _weekdayChips.SetFlags(masks);
            AddContent(_weekdayChips);

            _intervalField = new UiTextField();
            _intervalField.Text = (source.RecurrenceInterval > 0 ? source.RecurrenceInterval : 1).ToString();
            AddContent(_intervalField);

            _intervalUnit = new Label();
            _intervalUnit.Text = "日";
            _intervalUnit.ForeColor = Theme.TextMuted;
            _intervalUnit.Font = Theme.FontMeta;
            _intervalUnit.AutoSize = false;
            _intervalUnit.BackColor = Color.Transparent;
            AddContent(_intervalUnit);

            _secStyle = AddSectionLabel("提醒方式");
            _styleChips = new UiChipGroup();
            _styleChips.SetItems(new string[] { "右下角卡片", "全屏遮罩" });
            _styleChips.SelectedIndex = Math.Max(0, (int)source.Style);
            AddContent(_styleChips);

            // 启用与否是开关语义，放分区标题右侧，避免和形态胶囊挤在同一行。
            _enabledSwitch = new UiSwitchItem();
            _enabledSwitch.Text = "启用此任务";
            _enabledSwitch.Checked = source.Enabled;
            AddContent(_enabledSwitch);

            _footerHint = new Label();
            _footerHint.Text = "Enter 保存 · Esc 取消";
            _footerHint.ForeColor = Theme.TextFaint;
            _footerHint.Font = Theme.FontTag;
            _footerHint.AutoSize = false;
            _footerHint.BackColor = Color.Transparent;
            _footerHint.TextAlign = ContentAlignment.MiddleLeft;
            _footer.Controls.Add(_footerHint);

            _cancel = new UiButton();
            _cancel.Text = "取消";
            _cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            _footer.Controls.Add(_cancel);

            _save = new UiButton();
            _save.Primary = true;
            _save.Text = "保存";
            _save.Click += OnSaveClicked;
            _footer.Controls.Add(_save);

            AcceptButton = _save;
            CancelButton = _cancel;

            _recurrenceChips.SelectionChanged += delegate
            {
                UpdateRecurrenceControls();
                LayoutForm();
            };
            UpdateRecurrenceControls();
            LayoutForm();
        }

        public TodoTask Result { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);

            // TEMP-DIAG: 布局自检，定位后移除
            try
            {
                string line = string.Format(
                    "client={0}x{1} bounds={2} workArea={3} scale={4:0.00} contentMin={5}\n",
                    ClientSize.Width,
                    ClientSize.Height,
                    Bounds,
                    Screen.PrimaryScreen.WorkingArea,
                    Theme.ScaleFactor,
                    _content.AutoScrollMinSize);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xiaoding-layout.txt"),
                    line);
            }
            catch
            {
            }
        }

        private void AddContent(Control control)
        {
            _content.Controls.Add(control);
        }

        private Label AddSectionLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextFaint;
            label.Font = Theme.FontSection;
            label.AutoSize = false;
            label.Size = new Size(Theme.S(200), Theme.S(18));
            label.BackColor = Color.Transparent;
            AddContent(label);
            return label;
        }

        private Label AddFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextMuted;
            label.Font = Theme.FontMeta;
            label.AutoSize = false;
            label.Size = new Size(Theme.S(200), Theme.S(18));
            label.BackColor = Color.Transparent;
            AddContent(label);
            return label;
        }

        private Label AddHintLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextMuted;
            label.Font = Theme.FontTag;
            label.AutoSize = false;
            label.Size = new Size(Theme.S(160), Theme.S(18));
            label.TextAlign = ContentAlignment.MiddleRight;
            label.BackColor = Color.Transparent;
            AddContent(label);
            return label;
        }

        private void OnFooterPaint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
            }
        }

        /// <summary>按当前重复规则重新排布，隐藏的行不占高度；底栏始终贴底可见。</summary>
        private void LayoutForm()
        {
            RecurrenceKind kind = (RecurrenceKind)Math.Max(0, _recurrenceChips.SelectedIndex);

            int left = Theme.S(PadLeft);
            int width = Theme.S(ContentWidth);
            int right = left + width;
            int y = Theme.S(16);

            // 内容
            _secContent.Location = new Point(left, y);
            y += Theme.S(22);

            _lblTitle.Location = new Point(left, y);
            y += Theme.S(18);
            _titleField.SetBounds(left, y, width, Theme.S(34));
            y += Theme.S(34) + Theme.S(14);

            _lblNote.Location = new Point(left, y);
            y += Theme.S(18);
            _noteField.SetBounds(left, y, width, Theme.S(48));
            y += Theme.S(48) + Theme.S(14);

            _lblGroup.Location = new Point(left, y);
            y += Theme.S(18);
            _groupField.SetBounds(left, y, width, Theme.S(34));
            y += Theme.S(34) + Theme.S(18);

            // 时间
            _secTime.Location = new Point(left, y);
            y += Theme.S(22);

            _lblDate.Location = new Point(left, y);
            _lblTime.Location = new Point(Theme.S(354), y);
            y += Theme.S(18);
            _dateField.SetBounds(left, y, Theme.S(316), Theme.S(34));
            _timeField.SetBounds(Theme.S(354), y, Theme.S(180), Theme.S(34));
            y += Theme.S(34) + Theme.S(14);

            _lblRemind.Location = new Point(left, y);
            _remindHint.Location = new Point(right - Theme.S(160), y);
            y += Theme.S(18);
            _remindField.SetBounds(left, y, width - Theme.S(82), Theme.S(34));
            _remindUnit.SetBounds(right - Theme.S(72), y, Theme.S(72), Theme.S(34));
            y += Theme.S(34) + Theme.S(18);

            // 重复
            _secRecurrence.Location = new Point(left, y);
            y += Theme.S(22);

            _recurrenceChips.SetBounds(left, y, width, Theme.S(30));
            int chipsHeight = _recurrenceChips.PreferredHeight;
            _recurrenceChips.SetBounds(left, y, width, chipsHeight);
            y += chipsHeight + Theme.S(8);

            if (kind == RecurrenceKind.Custom)
            {
                _weekdayChips.SetBounds(left, y, width, Theme.S(30));
                y += Theme.S(38);
            }
            else if (kind == RecurrenceKind.MonthlyDay || kind == RecurrenceKind.EveryNDays)
            {
                _intervalField.SetBounds(left, y, Theme.S(90), Theme.S(34));
                _intervalUnit.SetBounds(Theme.S(102), y + Theme.S(8), Theme.S(60), Theme.S(18));
                y += Theme.S(44);
            }

            // 提醒方式：标题右侧放启用开关，下方只留两个形态胶囊。
            _secStyle.Location = new Point(left, y);
            _enabledSwitch.SetBounds(right - Theme.S(140), y - Theme.S(6), Theme.S(140), Theme.S(28));
            y += Theme.S(22);
            _styleChips.SetBounds(left, y, width, Theme.S(30));
            y += Theme.S(30) + Theme.S(12);

            int contentBottom = y + Theme.S(8);

            // 内容装不下时才滚动；窗口高度钳制在工作区内，底栏永远可见。
            _content.AutoScrollMinSize = new Size(0, contentBottom);
            int total = contentBottom + _footer.Height;
            int maxTotal = Screen.PrimaryScreen.WorkingArea.Height - Theme.S(52);
            ClientSize = new Size(Theme.S(FormWidth), Math.Min(total, maxTotal));
            LayoutFooter();
        }

        /// <summary>底栏按钮：右对齐，主次分明，36 高、26 右边距、10 间距；不依赖尚未布局的窗宽。</summary>
        private void LayoutFooter()
        {
            int footerWidth = Theme.S(FormWidth);
            int buttonHeight = Theme.S(36);
            int buttonY = Math.Max(0, (_footer.Height - buttonHeight) / 2);
            int marginRight = Theme.S(26);
            int gap = Theme.S(10);
            int saveWidth = Theme.S(108);
            int cancelWidth = Theme.S(84);

            _save.SetBounds(footerWidth - marginRight - saveWidth, buttonY, saveWidth, buttonHeight);
            _cancel.SetBounds(_save.Left - gap - cancelWidth, buttonY, cancelWidth, buttonHeight);
            _footerHint.SetBounds(Theme.S(26), buttonY, Math.Max(Theme.S(80), _cancel.Left - Theme.S(40)), buttonHeight);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_footer != null)
            {
                LayoutFooter();
            }
        }

        private void UpdateRecurrenceControls()
        {
            RecurrenceKind kind = (RecurrenceKind)Math.Max(0, _recurrenceChips.SelectedIndex);

            bool custom = kind == RecurrenceKind.Custom;
            bool monthly = kind == RecurrenceKind.MonthlyDay;
            bool everyN = kind == RecurrenceKind.EveryNDays;

            _weekdayChips.Visible = custom;
            _intervalField.Visible = monthly || everyN;
            _intervalUnit.Visible = monthly || everyN;
            _intervalUnit.Text = monthly ? "日" : "天";
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            string title = _titleField.Value;
            if (title.Length == 0)
            {
                UiMessageBox.Info(this, "请填写任务内容。");
                _titleField.FocusInput();
                return;
            }

            RecurrenceKind kind = (RecurrenceKind)Math.Max(0, _recurrenceChips.SelectedIndex);

            int mask = 0;
            foreach (int index in _weekdayChips.GetSelectedIndexes())
            {
                mask |= 1 << index;
            }

            if (kind == RecurrenceKind.Custom && mask == 0)
            {
                UiMessageBox.Info(this, "自定义重复至少选择一天。");
                return;
            }

            int interval = 1;
            int.TryParse(_intervalField.Value, out interval);
            if (kind == RecurrenceKind.MonthlyDay)
            {
                interval = Math.Max(1, Math.Min(31, interval));
            }
            else if (kind == RecurrenceKind.EveryNDays)
            {
                interval = Math.Max(2, Math.Min(365, interval));
            }

            int remind = 0;
            int.TryParse(_remindField.Value, out remind);
            remind = Math.Max(0, Math.Min(720, remind));

            DateTime local = _dateField.Value.Date.Add(_timeField.Value);

            TodoTask task = _original != null ? _original.Clone() : new TodoTask();
            task.Title = title;
            task.Note = _noteField.Value;
            task.Group = _groupField.Value;
            task.DueUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            task.RemindBeforeMinutes = remind;
            task.Recurrence = kind;
            task.WeekdaysMask = mask;
            task.RecurrenceInterval = (kind == RecurrenceKind.MonthlyDay || kind == RecurrenceKind.EveryNDays) ? interval : 0;
            task.Style = _styleChips.SelectedIndex == 1 ? ReminderStyle.FullScreen : ReminderStyle.Card;
            task.Enabled = _enabledSwitch.Checked;

            // 只有时间/重复/提前量变了才重新武装任务；否则改备注或分组会让已完成任务复活并再弹一次。
            bool scheduleChanged = _original == null
                || _original.DueUtc != task.DueUtc
                || _original.Recurrence != kind
                || _original.WeekdaysMask != mask
                || _original.RecurrenceInterval != task.RecurrenceInterval
                || _original.RemindBeforeMinutes != remind;

            if (scheduleChanged)
            {
                task.State = TaskState.Pending;
                task.SnoozeUntilUtc = null;
                task.LastFiredUtc = null;
                task.CompletedUtc = null;
            }

            Result = task;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static DateTime RoundUpToNextHalfHour(DateTime now)
        {
            DateTime baseTime = now.Date.AddHours(now.Hour);
            if (now.Minute < 30)
            {
                return baseTime.AddMinutes(30);
            }

            return baseTime.AddHours(1);
        }
    }
}
