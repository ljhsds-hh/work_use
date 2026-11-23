using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    /// <summary>左侧导航：品牌、视图、分组与设置入口。</summary>
    internal sealed class SideNav : Control
    {
        private sealed class Item
        {
            public int Kind;      // 0 = 视图, 1 = 分组, 2 = 设置
            public int ViewIndex;
            public string Group;
            public Rectangle Bounds;
            public bool Selected;
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<KeyValuePair<string, int>> _groups = new List<KeyValuePair<string, int>>();
        private int[] _counts = new int[4];
        private TaskView _view = TaskView.All;
        private string _group;
        private bool _dark;
        private Item _hover;

        private static readonly string[] ViewTexts = { "今天", "逾期", "全部", "已完成" };

        public SideNav()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.PaperMuted;
        }

        public event EventHandler<NavViewEventArgs> ViewRequested;

        public event EventHandler<NavGroupEventArgs> GroupRequested;

        public event EventHandler SettingsRequested;

        public event EventHandler ThemeToggleRequested;

        public void SetData(TaskView view, string group, int[] counts, List<KeyValuePair<string, int>> groups, bool dark)
        {
            _view = view;
            _group = group;
            _counts = counts ?? new int[4];
            _dark = dark;

            _groups.Clear();
            if (groups != null)
            {
                _groups.AddRange(groups);
            }

            RebuildItems();
            Invalidate();
        }

        private void RebuildItems()
        {
            _items.Clear();

            int y = Theme.S(78);
            int width = Width - Theme.S(20);

            // 视图固定四项：今天、逾期、全部、已完成。
            TaskView[] order = { TaskView.Today, TaskView.Overdue, TaskView.All, TaskView.Done };
            for (int i = 0; i < order.Length; i++)
            {
                Item item = new Item();
                item.Kind = 0;
                item.ViewIndex = i;
                item.Selected = _view == order[i];
                item.Bounds = new Rectangle(Theme.S(10), y, width, Theme.S(34));
                _items.Add(item);
                y += Theme.S(34);
            }

            y = Theme.S(256);

            int limit = Height - Theme.S(92);
            foreach (KeyValuePair<string, int> pair in _groups)
            {
                if (y + Theme.S(32) > limit)
                {
                    break;
                }

                Item item = new Item();
                item.Kind = 1;
                item.Group = pair.Key;
                item.Selected = _group != null && string.Equals(_group, pair.Key, StringComparison.CurrentCulture);
                item.Bounds = new Rectangle(Theme.S(10), y, width, Theme.S(32));
                _items.Add(item);
                y += Theme.S(32);
            }

            Item theme = new Item();
            theme.Kind = 3;
            theme.Bounds = new Rectangle(Theme.S(10), Height - Theme.S(78), width, Theme.S(32));
            _items.Add(theme);

            Item settings = new Item();
            settings.Kind = 2;
            settings.Bounds = new Rectangle(Theme.S(10), Height - Theme.S(44), width, Theme.S(32));
            _items.Add(settings);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildItems();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            using (SolidBrush background = new SolidBrush(Theme.PaperMuted))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            using (Pen edge = new Pen(Theme.Border))
            {
                graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            }

            DrawBrand(graphics);
            DrawSectionLabel(graphics, "视图", Theme.S(58));
            DrawSectionLabel(graphics, "分组", Theme.S(234));

            using (Pen divider = new Pen(Theme.Border))
            {
                graphics.DrawLine(divider, Theme.S(18), Theme.S(226), Width - Theme.S(18), Theme.S(226));
            }

            foreach (Item item in _items)
            {
                if (item.Kind != 2 && item.Kind != 3)
                {
                    DrawItem(graphics, item);
                }
            }

            foreach (Item item in _items)
            {
                if (item.Kind == 2 || item.Kind == 3)
                {
                    DrawItem(graphics, item);
                }
            }
        }

        private void DrawBrand(Graphics graphics)
        {
            Rectangle logo = new Rectangle(Theme.S(20), Theme.S(18), Theme.S(30), Theme.S(30));
            UiDraw.FillVerticalGradient(graphics, logo, Theme.SF(9), Theme.BrandShine, Theme.Brand);

            using (Font font = Theme.DisplayFont(10.5f, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    graphics.DrawString("叮", font, brush, logo, format);
                }
            }

            using (Font nameFont = Theme.DisplayFont(11f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    graphics,
                    "小叮",
                    nameFont,
                    new Rectangle(Theme.S(60), Theme.S(16), Theme.S(120), Theme.S(20)),
                    Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(
                graphics,
                "今天 · " + DateTime.Now.ToString("M月d日"),
                Theme.FontTag,
                new Rectangle(Theme.S(60), Theme.S(34), Theme.S(130), Theme.S(16)),
                Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawSectionLabel(Graphics graphics, string text, int y)
        {
            TextRenderer.DrawText(
                graphics,
                text,
                Theme.FontSection,
                new Rectangle(Theme.S(20), y, Theme.S(120), Theme.S(16)),
                Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawItem(Graphics graphics, Item item)
        {
            bool hover = item == _hover;

            if (item.Selected)
            {
                UiDraw.FillVerticalGradient(graphics, item.Bounds, Theme.SF(8), Theme.BrandSoft, Theme.BrandTint);
                // 左侧品牌竖条，标记当前视图。
                UiDraw.FillRoundedRect(
                    graphics,
                    new RectangleF(item.Bounds.X + Theme.S(2), item.Bounds.Y + Theme.S(8), Theme.SF(2.5f), item.Bounds.Height - Theme.S(16)),
                    Theme.SF(1.5f),
                    Theme.Brand);
            }
            else if (hover)
            {
                UiDraw.FillRoundedRect(graphics, item.Bounds, Theme.SF(8), Theme.Paper);
            }

            Color fore = item.Selected ? Theme.Brand : Theme.TextMuted;
            Color dot = item.Selected ? Theme.Brand : Theme.BorderStrong;

            if (item.Kind == 2 || item.Kind == 3)
            {
                fore = hover ? Theme.Text : Theme.TextMuted;
            }

            Rectangle dotRect = new Rectangle(
                item.Bounds.X + Theme.S(12),
                item.Bounds.Y + item.Bounds.Height / 2 - Theme.S(2),
                Theme.S(4),
                Theme.S(4));

            if (item.Kind == 0)
            {
                DrawViewIcon(graphics, item.ViewIndex, dotRect, fore);
            }
            else if (item.Kind == 1)
            {
                using (SolidBrush brush = new SolidBrush(dot))
                {
                    graphics.FillEllipse(brush, new Rectangle(
                        item.Bounds.X + Theme.S(11),
                        item.Bounds.Y + item.Bounds.Height / 2 - Theme.S(3),
                        Theme.S(6),
                        Theme.S(6)));
                }
            }
            else if (item.Kind == 2)
            {
                DrawGear(graphics, dotRect, fore);
            }
            else
            {
                DrawThemeIcon(graphics, dotRect, fore);
            }

            string text;
            string count = null;

            if (item.Kind == 0)
            {
                text = ViewTexts[item.ViewIndex];
                count = _counts[item.ViewIndex].ToString();
            }
            else if (item.Kind == 1)
            {
                text = item.Group;
                foreach (KeyValuePair<string, int> pair in _groups)
                {
                    if (string.Equals(pair.Key, item.Group, StringComparison.CurrentCulture))
                    {
                        count = pair.Value.ToString();
                        break;
                    }
                }
            }
            else if (item.Kind == 3)
            {
                text = _dark ? "浅色模式" : "深色模式";
            }
            else
            {
                text = "设置";
            }

            using (Font navFont = item.Selected
                ? new Font(Theme.FontNav, FontStyle.Bold)
                : Theme.FontNav)
            {
                TextRenderer.DrawText(
                    graphics,
                    text,
                    navFont,
                    new Rectangle(item.Bounds.X + Theme.S(24), item.Bounds.Y, item.Bounds.Width - Theme.S(64), item.Bounds.Height),
                    fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            if (!string.IsNullOrEmpty(count))
            {
                // 计数做成小徽章，数字不直接飘在底色上。
                Rectangle badge = new Rectangle(item.Bounds.Right - Theme.S(42), item.Bounds.Y + item.Bounds.Height / 2 - Theme.S(9), Theme.S(30), Theme.S(18));
                UiDraw.FillRoundedRect(
                    graphics,
                    badge,
                    Theme.SF(9),
                    item.Selected ? Theme.Brand : Theme.Paper);
                TextRenderer.DrawText(
                    graphics,
                    count,
                    Theme.FontMeta,
                    badge,
                    item.Selected ? Theme.BrandOn : Theme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>视图图标：今天=太阳，逾期=叹号，全部=三横，已完成=对勾，与设计稿一致。</summary>
        private static void DrawViewIcon(Graphics graphics, int viewIndex, Rectangle at, Color color)
        {
            Rectangle box = new Rectangle(at.X - Theme.S(5), at.Y - Theme.S(5), Theme.S(14), Theme.S(14));
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;

            using (Pen pen = new Pen(color, Theme.SF(1.3f)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                switch (viewIndex)
                {
                    case 0: // 今天：小太阳
                    {
                        float r = Theme.SF(3f);
                        graphics.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        for (int i = 0; i < 8; i++)
                        {
                            double angle = i * Math.PI / 4;
                            float x1 = cx + (float)(Math.Cos(angle) * (r + Theme.SF(2f)));
                            float y1 = cy + (float)(Math.Sin(angle) * (r + Theme.SF(2f)));
                            float x2 = cx + (float)(Math.Cos(angle) * (r + Theme.SF(4f)));
                            float y2 = cy + (float)(Math.Sin(angle) * (r + Theme.SF(4f)));
                            graphics.DrawLine(pen, x1, y1, x2, y2);
                        }

                        break;
                    }

                    case 1: // 逾期：叹号
                    {
                        graphics.DrawLine(pen, cx, cy - Theme.SF(5f), cx, cy + Theme.SF(1f));
                        graphics.FillEllipse(brush, cx - Theme.SF(1.2f), cy + Theme.SF(3f), Theme.SF(2.4f), Theme.SF(2.4f));
                        break;
                    }

                    case 2: // 全部：三横
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            graphics.DrawLine(pen, cx - Theme.SF(5f), cy + i * Theme.SF(3.5f), cx + Theme.SF(5f), cy + i * Theme.SF(3.5f));
                        }

                        break;
                    }

                    default: // 已完成：对勾
                    {
                        graphics.DrawLine(pen, cx - Theme.SF(4.5f), cy, cx - Theme.SF(1f), cy + Theme.SF(3.5f));
                        graphics.DrawLine(pen, cx - Theme.SF(1f), cy + Theme.SF(3.5f), cx + Theme.SF(5f), cy - Theme.SF(4f));
                        break;
                    }
                }
            }
        }

        private static void DrawGear(Graphics graphics, Rectangle at, Color color)
        {
            using (Pen pen = new Pen(color, 1.1f))
            {
                graphics.DrawEllipse(pen, at.X - 4, at.Y - 4, 12, 12);
                graphics.FillEllipse(new SolidBrush(color), at.X, at.Y, 4, 4);
            }
        }

        private static void DrawThemeIcon(Graphics graphics, Rectangle at, Color color)
        {
            Rectangle circle = new Rectangle(at.X - 4, at.Y - 4, 12, 12);
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillPie(brush, circle, 90f, 180f);
            }

            using (Pen pen = new Pen(color, 1.1f))
            {
                graphics.DrawEllipse(pen, circle);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item item = ItemAt(e.Location);
            if (!ReferenceEquals(item, _hover))
            {
                _hover = item;
                Cursor = item != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null)
            {
                _hover = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Item item = ItemAt(e.Location);
            if (item == null)
            {
                return;
            }

            switch (item.Kind)
            {
                case 0:
                {
                    TaskView[] order = { TaskView.Today, TaskView.Overdue, TaskView.All, TaskView.Done };
                    EventHandler<NavViewEventArgs> handler = ViewRequested;
                    if (handler != null)
                    {
                        handler(this, new NavViewEventArgs(order[item.ViewIndex]));
                    }

                    break;
                }

                case 1:
                {
                    EventHandler<NavGroupEventArgs> handler = GroupRequested;
                    if (handler != null)
                    {
                        handler(this, new NavGroupEventArgs(item.Group));
                    }

                    break;
                }

                case 2:
                {
                    EventHandler handler = SettingsRequested;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }

                    break;
                }

                default:
                {
                    EventHandler handler = ThemeToggleRequested;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }

                    break;
                }
            }
        }

        private Item ItemAt(Point point)
        {
            foreach (Item item in _items)
            {
                if (item.Bounds.Contains(point))
                {
                    return item;
                }
            }

            return null;
        }
    }

    internal sealed class NavViewEventArgs : EventArgs
    {
        public NavViewEventArgs(TaskView view)
        {
            View = view;
        }

        public TaskView View { get; private set; }
    }

    internal sealed class NavGroupEventArgs : EventArgs
    {
        public NavGroupEventArgs(string group)
        {
            Group = group;
        }

        public string Group { get; private set; }
    }
}
