using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>一个分组及其下的任务，顺序由主窗决定。</summary>
    internal sealed class TaskGroup
    {
        public TaskGroup(string name)
        {
            Name = name;
            Tasks = new List<TodoTask>();
        }

        public string Name { get; private set; }

        public List<TodoTask> Tasks { get; private set; }
    }

    /// <summary>
    /// 自绘任务列表：分组标题、行内标签、置顶底色、悬停与选中。
    /// 用 ScrollableControl 承载滚动，避免 ListView 无法定制的限制。
    /// </summary>
    internal sealed class TaskListView : ScrollableControl
    {
        private sealed class Row
        {
            public bool IsGroup;
            public string GroupName;
            public int GroupCount;
            public TodoTask Task;
            public Rectangle Bounds;
            public Rectangle Tick;
        }

        private readonly List<Row> _rows = new List<Row>();
        private TodoTask _selected;
        private TodoTask _hover;
        private bool _tickHover;

        public TaskListView()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            AutoScroll = true;
            BackColor = Theme.Surface;
            TabStop = true;
        }

        public event EventHandler<TaskEventArgs> TaskActivated;

        public event EventHandler<TaskEventArgs> TaskMenuRequested;

        public event EventHandler<TaskEventArgs> TaskDeleteRequested;

        public event EventHandler<TaskEventArgs> TaskToggleRequested;

        public TodoTask SelectedTask
        {
            get { return _selected; }
        }

        public int TaskCount
        {
            get
            {
                int count = 0;
                foreach (Row row in _rows)
                {
                    if (!row.IsGroup)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void SetGroups(List<TaskGroup> groups)
        {
            _rows.Clear();
            if (groups != null)
            {
                foreach (TaskGroup group in groups)
                {
                    if (group.Tasks.Count == 0)
                    {
                        continue;
                    }

                    Row header = new Row();
                    header.IsGroup = true;
                    header.GroupName = group.Name;
                    header.GroupCount = group.Tasks.Count;
                    _rows.Add(header);

                    foreach (TodoTask task in group.Tasks)
                    {
                        Row row = new Row();
                        row.Task = task;
                        _rows.Add(row);
                    }
                }
            }

            if (_selected != null && !ContainsTask(_selected))
            {
                _selected = null;
            }

            if (_hover != null && !ContainsTask(_hover))
            {
                _hover = null;
            }

            LayoutRows();
            Invalidate();
        }

        private bool ContainsTask(TodoTask task)
        {
            foreach (Row row in _rows)
            {
                if (!row.IsGroup && ReferenceEquals(row.Task, task))
                {
                    return true;
                }
            }

            return false;
        }

        private void LayoutRows()
        {
            int width = Math.Max(Theme.S(200), ClientSize.Width - Theme.S(16));
            int y = Theme.S(4);

            foreach (Row row in _rows)
            {
                int height = row.IsGroup ? Theme.S(34) : Theme.S(62);
                row.Bounds = new Rectangle(Theme.S(6), y, width, height);

                if (!row.IsGroup)
                {
                    row.Tick = new Rectangle(
                        row.Bounds.X + Theme.S(14),
                        row.Bounds.Y + Theme.S(14),
                        Theme.S(17),
                        Theme.S(17));
                }

                y += height;
            }

            AutoScrollMinSize = new Size(0, y + Theme.S(10));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRows();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            using (SolidBrush background = new SolidBrush(Theme.Surface))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            int scrollY = -AutoScrollPosition.Y;
            graphics.TranslateTransform(0, scrollY);

            foreach (Row row in _rows)
            {
                if (row.Bounds.Bottom < scrollY || row.Bounds.Top > scrollY + ClientSize.Height)
                {
                    continue;
                }

                if (row.IsGroup)
                {
                    DrawGroupHeader(graphics, row, row.Bounds);
                }
                else
                {
                    DrawTaskRow(graphics, row);
                }
            }

            graphics.ResetTransform();
            DrawStickyHeader(graphics, scrollY);
        }

        private void DrawStickyHeader(Graphics graphics, int scrollY)
        {
            Row sticky = null;
            foreach (Row row in _rows)
            {
                if (row.IsGroup)
                {
                    if (row.Bounds.Top - scrollY <= Theme.S(2))
                    {
                        sticky = row;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (sticky == null || sticky.Bounds.Top - scrollY >= 0)
            {
                return;
            }

            Rectangle area = new Rectangle(0, 0, ClientSize.Width - Theme.S(2), Theme.S(34));
            using (SolidBrush background = new SolidBrush(Theme.Surface))
            {
                graphics.FillRectangle(background, area);
            }

            DrawGroupHeader(graphics, sticky, new Rectangle(Theme.S(6), 0, area.Width - Theme.S(6), Theme.S(34)));
        }

        private void DrawGroupHeader(Graphics graphics, Row row, Rectangle bounds)
        {
            int left = bounds.X + Theme.S(10);
            int right = bounds.Right - Theme.S(10);
            int countWidth = Theme.S(30);

            Size nameSize = TextRenderer.MeasureText(
                graphics,
                row.GroupName,
                Theme.FontSection,
                new Size(int.MaxValue, Theme.S(20)),
                TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                graphics,
                row.GroupName,
                Theme.FontSection,
                new Rectangle(left, bounds.Y, nameSize.Width + Theme.S(4), bounds.Height),
                Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            string count = row.GroupCount.ToString();
            TextRenderer.DrawText(
                graphics,
                count,
                Theme.FontMeta,
                new Rectangle(right - countWidth, bounds.Y, countWidth, bounds.Height),
                Theme.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int lineLeft = left + nameSize.Width + Theme.S(10);
            int lineRight = right - countWidth - Theme.S(10);
            if (lineRight > lineLeft)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    int lineY = bounds.Y + bounds.Height / 2;
                    graphics.DrawLine(pen, lineLeft, lineY, lineRight, lineY);
                }
            }
        }

        private void DrawTaskRow(Graphics graphics, Row row)
        {
            TodoTask task = row.Task;
            Rectangle bounds = row.Bounds;
            Rectangle inner = new Rectangle(
                bounds.X + Theme.S(2),
                bounds.Y + Theme.S(2),
                bounds.Width - Theme.S(4),
                bounds.Height - Theme.S(4));

            bool selected = ReferenceEquals(task, _selected);
            bool hover = ReferenceEquals(task, _hover);
            bool done = task.State == TaskState.Done;
            float radius = Theme.SF(10);

            if (task.Pinned)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    inner,
                    Theme.BrandSoft,
                    Theme.Surface,
                    LinearGradientMode.Horizontal))
                {
                    using (GraphicsPath path = UiDraw.RoundedRect(inner, radius))
                    {
                        graphics.FillPath(brush, path);
                    }
                }

                UiDraw.FillRoundedRect(
                    graphics,
                    new RectangleF(inner.X + Theme.S(2), inner.Y + Theme.S(12), Theme.S(2), inner.Height - Theme.S(24)),
                    Theme.SF(1),
                    Theme.Brand);
            }
            else if (selected)
            {
                UiDraw.FillRoundedRect(graphics, inner, radius, Theme.Selection);
                // 选中行左侧品牌竖条。
                UiDraw.FillRoundedRect(
                    graphics,
                    new RectangleF(inner.X + Theme.S(2), inner.Y + Theme.S(14), Theme.SF(2.5f), inner.Height - Theme.S(28)),
                    Theme.SF(1.5f),
                    Theme.Brand);
            }
            else if (hover)
            {
                UiDraw.FillRoundedRect(graphics, inner, radius, Theme.SurfaceMuted);
            }

            DrawTick(graphics, row.Tick, done);

            int textLeft = row.Tick.Right + Theme.S(11);
            int timeWidth = Theme.S(84);
            int textRight = bounds.Right - Theme.S(8) - timeWidth;

            Color titleColor = done ? Theme.TextFaint : Theme.Text;
            string title = task.Pinned ? "★ " + task.Title : task.Title;

            using (Font titleFont = done
                ? new Font(Theme.FontRowTitle, FontStyle.Strikeout)
                : Theme.FontRowTitle)
            {
                TextRenderer.DrawText(
                    graphics,
                    title,
                    titleFont,
                    new Rectangle(textLeft, bounds.Y + Theme.S(8), Math.Max(Theme.S(40), textRight - textLeft), Theme.S(22)),
                    titleColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            int tagX = textLeft;
            int tagY = bounds.Y + Theme.S(32);

            string groupName = string.IsNullOrWhiteSpace(task.Group) ? "默认" : task.Group;
            tagX = DrawTag(graphics, tagX, tagY, groupName, Theme.BrandSoft, Theme.Brand);

            string style = task.Style == ReminderStyle.FullScreen ? "全屏" : "卡片";
            tagX = DrawTag(graphics, tagX, tagY, style, Theme.PaperMuted, Theme.TextMuted);

            string recurrence = RecurrenceCalculator.Describe(task.Recurrence, task.WeekdaysMask, task.RecurrenceInterval);
            if (tagX + Theme.S(56) < textRight)
            {
                DrawTag(graphics, tagX, tagY, recurrence, Theme.PaperMuted, Theme.TextMuted);
            }

            DrawTime(graphics, bounds, task);
        }

        private int DrawTag(Graphics graphics, int x, int y, string text, Color fill, Color fore)
        {
            Size size = TextRenderer.MeasureText(graphics, text, Theme.FontTag, new Size(int.MaxValue, Theme.S(20)), TextFormatFlags.NoPadding);
            int width = size.Width + Theme.S(14);
            int height = Theme.S(18);
            Rectangle bounds = new Rectangle(x, y, width, height);

            UiDraw.FillRoundedRect(graphics, bounds, Theme.SF(5), fill);
            TextRenderer.DrawText(
                graphics,
                text,
                Theme.FontTag,
                bounds,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            return x + width + Theme.S(6);
        }

        private void DrawTime(Graphics graphics, Rectangle bounds, TodoTask task)
        {
            DateTime local = DateTime.SpecifyKind(task.DueUtc, DateTimeKind.Utc).ToLocalTime();
            bool overdue = task.State != TaskState.Done && task.DueUtc < DateTime.UtcNow;
            Color color = overdue ? Theme.Amber : (task.State == TaskState.Done ? Theme.TextFaint : Theme.TextMuted);
            string text = FormatDue(local);

            Rectangle timeRect = new Rectangle(bounds.Right - Theme.S(90), bounds.Y + Theme.S(8), Theme.S(84), Theme.S(22));

            // 逾期时间垫一层暖色底，不靠颜色单独扛语义。
            if (overdue)
            {
                Size measured = TextRenderer.MeasureText(graphics, text, Theme.FontMeta, new Size(int.MaxValue, timeRect.Height), TextFormatFlags.NoPadding);
                Rectangle chip = new Rectangle(
                    timeRect.Right - Math.Min(timeRect.Width, measured.Width + Theme.S(12)),
                    timeRect.Y + Theme.S(1),
                    Math.Min(timeRect.Width, measured.Width + Theme.S(12)),
                    timeRect.Height - Theme.S(2));
                UiDraw.FillRoundedRect(graphics, chip, Theme.SF(6), Theme.AmberSoft);
            }

            TextRenderer.DrawText(
                graphics,
                text,
                Theme.FontMeta,
                timeRect,
                color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawTick(Graphics graphics, Rectangle tick, bool done)
        {
            if (done)
            {
                UiDraw.FillVerticalGradient(graphics, tick, tick.Width / 2f, Theme.BrandShine, Theme.Brand);

                using (Pen pen = new Pen(Color.White, Theme.SF(1.6f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawLines(
                        pen,
                        new Point[]
                        {
                            new Point(tick.X + (int)Theme.SF(4.5f), tick.Y + tick.Height / 2),
                            new Point(tick.X + tick.Width / 2 - 1, tick.Bottom - (int)Theme.SF(5f)),
                            new Point(tick.Right - (int)Theme.SF(4f), tick.Y + (int)Theme.SF(5f))
                        });
                }

                return;
            }

            Color border = _tickHover ? Theme.Brand : Theme.BorderStrong;
            using (Pen pen = new Pen(border, Theme.SF(1.4f)))
            {
                graphics.DrawEllipse(pen, tick);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            TodoTask hover = TaskAt(e.Location);
            bool tickHover = false;
            if (hover != null)
            {
                Row row = RowOf(hover);
                tickHover = row != null && row.Tick.Contains(e.Location);
            }

            if (!ReferenceEquals(hover, _hover) || tickHover != _tickHover)
            {
                _hover = hover;
                _tickHover = tickHover;
                Cursor = tickHover ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null || _tickHover)
            {
                _hover = null;
                _tickHover = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            Row row = RowAt(e.Location);
            if (row == null || row.IsGroup)
            {
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _selected = row.Task;
                Invalidate();

                EventHandler<TaskEventArgs> handler = TaskMenuRequested;
                if (handler != null)
                {
                    handler(this, new TaskEventArgs(row.Task));
                }

                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (row.Tick.Contains(e.Location))
            {
                EventHandler<TaskEventArgs> handler = TaskToggleRequested;
                if (handler != null)
                {
                    handler(this, new TaskEventArgs(row.Task));
                }

                return;
            }

            if (!ReferenceEquals(_selected, row.Task))
            {
                _selected = row.Task;
                Invalidate();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            Row row = RowAt(e.Location);
            if (row == null || row.IsGroup || row.Tick.Contains(e.Location))
            {
                return;
            }

            EventHandler<TaskEventArgs> handler = TaskActivated;
            if (handler != null)
            {
                handler(this, new TaskEventArgs(row.Task));
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Delete || keyData == Keys.Enter)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_selected == null)
            {
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                EventHandler<TaskEventArgs> handler = TaskDeleteRequested;
                if (handler != null)
                {
                    handler(this, new TaskEventArgs(_selected));
                }

                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                EventHandler<TaskEventArgs> handler = TaskActivated;
                if (handler != null)
                {
                    handler(this, new TaskEventArgs(_selected));
                }

                e.Handled = true;
            }
        }

        private Row RowAt(Point point)
        {
            Point local = new Point(point.X, point.Y - AutoScrollPosition.Y);
            foreach (Row row in _rows)
            {
                if (row.Bounds.Contains(local))
                {
                    return row;
                }
            }

            return null;
        }

        private TodoTask TaskAt(Point point)
        {
            Row row = RowAt(point);
            return row != null && !row.IsGroup ? row.Task : null;
        }

        private Row RowOf(TodoTask task)
        {
            foreach (Row row in _rows)
            {
                if (!row.IsGroup && ReferenceEquals(row.Task, task))
                {
                    return row;
                }
            }

            return null;
        }

        private static string FormatDue(DateTime local)
        {
            DateTime today = DateTime.Today;
            string prefix;
            if (local.Date == today)
            {
                prefix = "今天 ";
            }
            else if (local.Date == today.AddDays(1))
            {
                prefix = "明天 ";
            }
            else
            {
                prefix = local.ToString("MM-dd ");
            }

            return prefix + local.ToString("HH:mm");
        }
    }
}
