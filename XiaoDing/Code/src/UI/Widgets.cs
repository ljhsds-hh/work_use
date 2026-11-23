using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    /// <summary>圆角按钮。主按钮实心墨绿，次按钮描边。</summary>
    internal sealed class UiButton : Control, IButtonControl
    {
        private bool _hover;
        private bool _pressed;
        private DialogResult _dialogResult = DialogResult.None;

        public UiButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = Theme.FontButton;
            Height = Theme.S(34);
        }

        public bool Primary { get; set; }

        /// <summary>危险操作（删除等）：主按钮用暖赭色渐变。</summary>
        public bool Danger { get; set; }

        /// <summary>链接样式：无边框，文字带虚线下划线，用于状态栏里的轻量操作。</summary>
        public bool Link { get; set; }

        public DialogResult DialogResult
        {
            get { return _dialogResult; }
            set { _dialogResult = value; }
        }

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick()
        {
            OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Link)
            {
                Color linkFore = _hover ? Theme.Brand : Theme.TextMuted;
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    Rectangle.Inflate(bounds, -Theme.S(6), 0),
                    linkFore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                Size measured = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
                int lineY = Height / 2 + measured.Height / 2 + Theme.S(2);
                int lineHalf = measured.Width / 2;
                using (Pen pen = new Pen(_hover ? Theme.Brand : Theme.BorderStrong, 1f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    graphics.DrawLine(pen, Width / 2 - lineHalf, lineY, Width / 2 + lineHalf, lineY);
                }

                return;
            }

            Color fill;
            Color fore;
            Color border = Color.Transparent;
            float borderWidth = 0f;
            RectangleF body = new RectangleF(0, 0, Width - 1, Height - 1);

            if (Primary)
            {
                Color top;
                Color bottom;
                if (Danger)
                {
                    bottom = Theme.Amber;
                    top = _pressed
                        ? Lerp(Theme.Amber, Color.Black, 0.18f)
                        : (_hover ? Lerp(Theme.Amber, Color.White, 0.12f) : Lerp(Theme.Amber, Color.White, 0.2f));
                    fore = Color.White;
                }
                else
                {
                    bottom = Theme.Brand;
                    top = _pressed ? Theme.BrandDark : (_hover ? Theme.BrandHover : Theme.BrandShine);
                    fore = Theme.BrandOn;
                }

                UiDraw.FillVerticalGradient(graphics, body, Theme.SF(9), top, bottom);

                if (!_pressed)
                {
                    // 静置态垫一层极浅投影，让主按钮有“浮起”的层次
                    RectangleF under = RectangleF.Inflate(body, 0f, Theme.SF(1f));
                    under.Y += Theme.SF(1.5f);
                    using (GraphicsPath shadowPath = UiDraw.RoundedRect(under, Theme.SF(9)))
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(28, Theme.ShadowTint)))
                    {
                        graphics.FillPath(shadow, shadowPath);
                    }
                }
            }
            else
            {
                fill = _pressed ? Theme.PaperMuted : (_hover ? Theme.SurfaceMuted : Theme.Surface);
                fore = _hover ? Theme.Brand : Theme.Text;
                border = _hover ? Theme.Brand : Theme.BorderStrong;
                borderWidth = 1f;
                UiDraw.DrawRoundedRect(graphics, body, Theme.SF(9), fill, border, borderWidth);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                Rectangle.Inflate(bounds, -Theme.S(6), 0),
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused && !Link)
            {
                UiDraw.DrawFocusRing(graphics, body, Theme.SF(9), Theme.Brand);
            }
        }

        private static Color Lerp(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            bool wasPressed = _pressed;
            _pressed = false;
            Invalidate();

            if (wasPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                return true;
            }

            return base.IsInputKey(keyData);
        }
    }

    /// <summary>带圆角边框与放大镜图标的搜索框。</summary>
    internal sealed class UiSearchBox : Panel
    {
        private readonly TextBox _input;
        private bool _focused;

        public UiSearchBox()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            Height = Theme.S(32);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.Font = Theme.FontBody;
            // 必须与外框绘制的底色一致，否则原生文本框会露出一块更浅的白底。
            _input.BackColor = Theme.SurfaceMuted;
            _input.ForeColor = Theme.Text;
            _input.SetBounds(Theme.S(30), Theme.S(7), Width - Theme.S(40), Theme.S(20));
            _input.TextChanged += delegate
            {
                EventHandler handler = TextChangedEvent;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _input.GotFocus += delegate { _focused = true; Invalidate(); };
            _input.LostFocus += delegate { _focused = false; Invalidate(); };
            Controls.Add(_input);
        }

        public event EventHandler TextChangedEvent;

        public string Query
        {
            get { return _input.Text.Trim(); }
        }

        public void SetCue(string text)
        {
            SendMessage(_input.Handle, 0x1501, new IntPtr(1), text);
        }

        public void Clear()
        {
            _input.Clear();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_input != null)
            {
                _input.SetBounds(Theme.S(30), Math.Max(Theme.S(5), (Height - Theme.S(20)) / 2), Width - Theme.S(40), Theme.S(20));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(
                graphics,
                bounds,
                Theme.SF(9),
                Theme.SurfaceMuted,
                _focused ? Theme.Brand : Theme.Border,
                1f);

            using (Pen pen = new Pen(Theme.TextFaint, Theme.SF(1.2f)))
            {
                int cx = Theme.S(15);
                int cy = Height / 2;
                int r = Theme.S(5);
                graphics.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                graphics.DrawLine(pen, cx + r - 1, cy + r - 1, cx + r + 3, cy + r + 3);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>开关项：左侧滑动开关，右侧文字，整行可点。</summary>
    internal sealed class UiSwitchItem : Control
    {
        private bool _checked;
        private bool _hover;

        public UiSwitchItem()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint,
                true);
            Cursor = Cursors.Hand;
            Font = Theme.FontMeta;
            Height = Theme.S(24);
        }

        public event EventHandler CheckedChangedEvent;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _checked = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            int trackWidth = Theme.S(30);
            int trackHeight = Theme.S(17);
            Rectangle track = new Rectangle(0, (Height - trackHeight) / 2, trackWidth, trackHeight);

            UiDraw.FillRoundedRect(
                graphics,
                track,
                trackHeight / 2f,
                _checked ? Theme.Brand : Theme.BorderStrong);

            int knobSize = Theme.S(13);
            int knobX = _checked ? track.Right - knobSize - Theme.S(2) : track.X + Theme.S(2);
            Rectangle knob = new Rectangle(knobX, track.Y + Theme.S(2), knobSize, knobSize);

            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(brush, knob);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(trackWidth + Theme.S(9), 0, Width - trackWidth - Theme.S(9), Height),
                _hover ? Theme.Text : Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _checked = !_checked;
            Invalidate();

            EventHandler handler = CheckedChangedEvent;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>自绘右键菜单，替代系统 ContextMenuStrip，保持纸感配色与圆角。</summary>
    internal sealed class UiMenu : PopupPanel
    {
        private sealed class Entry
        {
            public string Text;
            public Action Click;
            public bool Danger;
            public Rectangle Bounds;
        }

        private const int ItemHeight = 32;
        private const int PadY = 6;
        private const int WidthDip = 148;

        private readonly List<Entry> _entries = new List<Entry>();
        private int _hover = -1;

        public UiMenu()
        {
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            ClientSize = new Size(Theme.S(WidthDip), Theme.S(PadY * 2));
        }

        public void Add(string text, Action click)
        {
            Add(text, click, false);
        }

        public void Add(string text, Action click, bool danger)
        {
            Entry entry = new Entry();
            entry.Text = text;
            entry.Click = click;
            entry.Danger = danger;
            _entries.Add(entry);

            ClientSize = new Size(Theme.S(WidthDip), Theme.S(PadY * 2 + _entries.Count * ItemHeight));
            Relayout();
        }

        private void Relayout()
        {
            int y = Theme.S(PadY);
            foreach (Entry entry in _entries)
            {
                entry.Bounds = new Rectangle(Theme.S(5), y, ClientSize.Width - Theme.S(10), Theme.S(ItemHeight));
                y += Theme.S(ItemHeight);
            }
        }

        /// <summary>在鼠标位置弹出，自动避让屏幕边缘。</summary>
        public void ShowAt(Point screenPoint)
        {
            Rectangle area = Screen.FromPoint(screenPoint).WorkingArea;
            int x = Math.Min(screenPoint.X, area.Right - Width - Theme.S(6));
            int y = Math.Min(screenPoint.Y, area.Bottom - Height - Theme.S(6));
            Location = new Point(
                Math.Max(area.Left + Theme.S(4), x),
                Math.Max(area.Top + Theme.S(4), y));
            Show();
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (i == _hover)
                {
                    UiDraw.FillRoundedRect(graphics, entry.Bounds, Theme.SF(6), Theme.PaperMuted);
                }

                TextRenderer.DrawText(
                    graphics,
                    entry.Text,
                    Theme.FontBody,
                    Rectangle.Inflate(entry.Bounds, -Theme.S(11), 0),
                    entry.Danger ? Theme.Amber : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hover = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Bounds.Contains(e.Location))
                {
                    hover = i;
                    break;
                }
            }

            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
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

            Entry hit = null;
            foreach (Entry entry in _entries)
            {
                if (entry.Bounds.Contains(e.Location))
                {
                    hit = entry;
                    break;
                }
            }

            if (hit == null)
            {
                return;
            }

            Close();
            if (hit.Click != null)
            {
                hit.Click();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>列表为空时的引导画面。</summary>
    internal sealed class EmptyStateView : Control
    {
        public EmptyStateView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.Surface;
        }

        public string Title { get; set; }

        public string Description { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            using (SolidBrush background = new SolidBrush(Theme.Surface))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            int artSize = Theme.S(96);
            int total = artSize + Theme.S(18) + Theme.S(26) + Theme.S(10) + Theme.S(20) + Theme.S(14) + Theme.S(26);
            int top = Math.Max(Theme.S(10), (Height - total) / 2);
            int centerX = Width / 2;

            Rectangle art = new Rectangle(centerX - artSize / 2, top, artSize, artSize);
            UiDraw.DrawSoftShadow(graphics, art, Theme.SF(28), 5, Theme.S(8), 26);
            UiDraw.FillVerticalGradient(graphics, art, Theme.SF(28), Theme.Surface, Theme.SurfaceMuted);
            using (Pen edge = new Pen(Color.FromArgb(120, Theme.Border)))
            {
                graphics.DrawEllipse(edge, art.X + Theme.SF(1f), art.Y + Theme.SF(1f), art.Width - Theme.SF(2f), art.Height - Theme.SF(2f));
            }

            using (Pen pen = new Pen(Color.FromArgb(70, Theme.Brand), Theme.SF(1.6f)))
            {
                pen.DashStyle = DashStyle.Dash;
                int ring = Theme.S(38);
                graphics.DrawEllipse(pen, centerX - ring / 2, top + artSize / 2 - ring / 2, ring, ring);
            }

            using (Pen pen = new Pen(Color.FromArgb(40, Theme.Brand), Theme.SF(1.2f)))
            {
                int ring = Theme.S(58);
                graphics.DrawEllipse(pen, centerX - ring / 2, top + artSize / 2 - ring / 2, ring, ring);
            }

            Rectangle dotRect = new Rectangle(art.Right - Theme.S(28), art.Top + Theme.S(20), Theme.S(8), Theme.S(8));
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(50, Theme.Brand)))
            {
                graphics.FillEllipse(glow, Rectangle.Inflate(dotRect, Theme.S(3), Theme.S(3)));
            }

            using (SolidBrush dot = new SolidBrush(Theme.Brand))
            {
                graphics.FillEllipse(dot, dotRect);
            }

            int y = art.Bottom + Theme.S(18);

            using (Font titleFont = Theme.DisplayFont(14f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    graphics,
                    string.IsNullOrEmpty(Title) ? "今天还没有安排" : Title,
                    titleFont,
                    new Rectangle(0, y, Width, Theme.S(26)),
                    Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            y += Theme.S(30);

            TextRenderer.DrawText(
                graphics,
                string.IsNullOrEmpty(Description) ? "按快捷键随手记一条，剩下的交给小叮。" : Description,
                Theme.FontBody,
                new Rectangle(0, y, Width, Theme.S(20)),
                Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            y += Theme.S(30);

            DrawKeyHints(graphics, centerX, y);
        }

        private static void DrawKeyHints(Graphics graphics, int centerX, int y)
        {
            string[] keys = { "Ctrl", "Alt", "N" };
            int gap = Theme.S(5);
            int keyHeight = Theme.S(21);
            int totalWidth = 0;
            int[] widths = new int[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                Size size = TextRenderer.MeasureText(graphics, keys[i], Theme.FontTag, new Size(int.MaxValue, keyHeight), TextFormatFlags.NoPadding);
                widths[i] = size.Width + Theme.S(14);
                totalWidth += widths[i] + (i < keys.Length - 1 ? gap : 0);
            }

            Size labelSize = TextRenderer.MeasureText(graphics, "快速添加", Theme.FontTag, new Size(int.MaxValue, keyHeight), TextFormatFlags.NoPadding);
            totalWidth += Theme.S(8) + labelSize.Width;

            int x = centerX - totalWidth / 2;

            for (int i = 0; i < keys.Length; i++)
            {
                Rectangle key = new Rectangle(x, y, widths[i], keyHeight);
                // 键帽：上浅下深的微渐变 + 描边，模拟实体键帽质感。
                UiDraw.FillVerticalGradient(graphics, key, Theme.SF(5), Theme.Surface, Theme.SurfaceMuted);
                using (Pen border = new Pen(Theme.BorderStrong, 1f))
                {
                    using (GraphicsPath path = UiDraw.RoundedRect(key, Theme.SF(5)))
                    {
                        graphics.DrawPath(border, path);
                    }
                }

                TextRenderer.DrawText(
                    graphics,
                    keys[i],
                    Theme.FontTag,
                    key,
                    Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += widths[i] + gap;
            }

            TextRenderer.DrawText(
                graphics,
                "快速添加",
                Theme.FontTag,
                new Rectangle(x + Theme.S(3), y, labelSize.Width + Theme.S(6), keyHeight),
                Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
