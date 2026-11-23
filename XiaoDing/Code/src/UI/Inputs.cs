using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    /// <summary>圆角输入框，内部是无边框文本框，避免露出系统控件样式。</summary>
    internal sealed class UiTextField : Control
    {
        private readonly TextBox _box;
        private bool _focused;

        public UiTextField()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(36);
            BackColor = Theme.SurfaceMuted;

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Font = Theme.FontBody;
            _box.BackColor = Theme.SurfaceMuted;
            _box.ForeColor = Theme.Text;
            _box.TextChanged += delegate
            {
                EventHandler handler = TextChangedEvent;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _box.GotFocus += delegate { _focused = true; Invalidate(); };
            _box.LostFocus += delegate { _focused = false; Invalidate(); };
            _box.KeyPress += delegate(object sender, KeyPressEventArgs e)
            {
                if (NumericOnly && !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            };
            Controls.Add(_box);
        }

        public event EventHandler TextChangedEvent;

        /// <summary>只允许输入数字，用于时间、提前量这类数值字段。</summary>
        public bool NumericOnly { get; set; }

        /// <summary>文本对齐方式。</summary>
        public HorizontalAlignment InputAlign
        {
            get { return _box.TextAlign; }
            set { _box.TextAlign = value; }
        }

        public bool Multiline
        {
            get { return _box.Multiline; }
            set
            {
                _box.Multiline = value;
                _box.ScrollBars = ScrollBars.None;
                LayoutBox();
            }
        }

        public int MaxLength
        {
            get { return _box.MaxLength; }
            set { _box.MaxLength = value; }
        }

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value; }
        }

        public string Value
        {
            get { return _box.Text.Trim(); }
        }

        public void FocusInput()
        {
            _box.Focus();
            // 聚焦时把光标放到末尾，避免整段文字被高亮成系统蓝色选中态。
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBox();
        }

        private void LayoutBox()
        {
            if (_box == null)
            {
                return;
            }

            int padding = Theme.S(12);
            _box.SetBounds(padding, Theme.S(8), Math.Max(Theme.S(20), Width - padding * 2), Math.Max(Theme.S(18), Height - Theme.S(16)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(
                graphics,
                bounds,
                Theme.SF(8),
                Theme.SurfaceMuted,
                _focused ? Theme.Brand : Theme.Border,
                1f);
        }
    }

    /// <summary>只读单位方块，与输入框同高，右侧显示「分钟」「日」「天」。</summary>
    internal sealed class UiUnitBox : Control
    {
        public UiUnitBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(36);
            BackColor = Theme.Surface;
            Font = Theme.FontMeta;
            ForeColor = Theme.TextMuted;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(graphics, bounds, Theme.SF(8), Theme.SurfaceMuted, Theme.Border, 1f);

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                bounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>胶囊选择组，单选或多选，自动换行。</summary>
    internal sealed class UiChipGroup : Control
    {
        private sealed class Chip
        {
            public string Text;
            public Rectangle Bounds;
        }

        private readonly List<Chip> _chips = new List<Chip>();
        private string[] _items = new string[0];
        private int _selected = -1;
        private bool[] _flags = new bool[0];
        private int _hover = -1;

        public UiChipGroup()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(30);
            BackColor = Theme.Surface;
        }

        public event EventHandler SelectionChanged;

        public bool MultiSelect { get; set; }

        public bool AllowDeselect { get; set; }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                Invalidate();
            }
        }

        public void SetItems(string[] items)
        {
            _items = items ?? new string[0];
            _flags = new bool[_items.Length];
            BuildChips();
        }

        public void SetFlags(bool[] flags)
        {
            if (flags == null)
            {
                return;
            }

            _flags = flags;
            Invalidate();
        }

        /// <summary>按当前宽度换行后实际占用的高度。</summary>
        public int PreferredHeight
        {
            get
            {
                int bottom = Theme.S(30);
                foreach (Chip chip in _chips)
                {
                    bottom = Math.Max(bottom, chip.Bounds.Bottom);
                }

                return bottom;
            }
        }

        /// <summary>按当前排布实际占用的宽度，用于同一行内紧跟其它控件。</summary>
        public int PreferredWidth
        {
            get
            {
                int right = 0;
                foreach (Chip chip in _chips)
                {
                    right = Math.Max(right, chip.Bounds.Right);
                }

                return right;
            }
        }

        public int[] GetSelectedIndexes()
        {
            List<int> result = new List<int>();
            for (int i = 0; i < _flags.Length; i++)
            {
                if (_flags[i])
                {
                    result.Add(i);
                }
            }

            return result.ToArray();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            BuildChips();
        }

        private void BuildChips()
        {
            _chips.Clear();
            int x = 0;
            int y = 0;
            int lineHeight = Theme.S(30);

            using (Graphics graphics = CreateGraphics())
            {
                foreach (string item in _items)
                {
                    Size size = TextRenderer.MeasureText(graphics, item, Theme.FontTag, new Size(int.MaxValue, Theme.S(20)), TextFormatFlags.NoPadding);
                    int width = size.Width + Theme.S(22);

                    if (x > 0 && x + width > Width)
                    {
                        x = 0;
                        y += lineHeight + Theme.S(6);
                    }

                    Chip chip = new Chip();
                    chip.Text = item;
                    chip.Bounds = new Rectangle(x, y, width, Theme.S(30));
                    _chips.Add(chip);
                    x += width + Theme.S(6);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            for (int i = 0; i < _chips.Count; i++)
            {
                bool on = MultiSelect ? (_flags.Length > i && _flags[i]) : (_selected == i);
                bool hover = _hover == i;

                Color fill = on ? Theme.BrandSoft : Theme.SurfaceMuted;
                Color border = on ? Theme.Brand : (hover ? Theme.BorderStrong : Theme.Border);
                Color fore = on ? Theme.Brand : Theme.TextMuted;

                UiDraw.DrawRoundedRect(graphics, _chips[i].Bounds, Theme.SF(6), fill, border, 1f);
                TextRenderer.DrawText(
                    graphics,
                    _chips[i].Text,
                    Theme.FontTag,
                    _chips[i].Bounds,
                    fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index != _hover)
            {
                _hover = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover >= 0)
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

            int index = HitTest(e.Location);
            if (index < 0)
            {
                return;
            }

            if (MultiSelect)
            {
                if (index < _flags.Length)
                {
                    _flags[index] = !_flags[index];
                }
            }
            else if (AllowDeselect && _selected == index)
            {
                _selected = -1;
            }
            else
            {
                _selected = index;
            }

            Invalidate();

            EventHandler handler = SelectionChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                if (_chips[i].Bounds.Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>无边框弹出面板，失去焦点时自动关闭。</summary>
    internal abstract class PopupPanel : Form
    {
        private bool _ready;

        protected PopupPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080;
                parameters.ClassStyle |= 0x00020000;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            using (GraphicsPath path = UiDraw.RoundedRect(new RectangleF(0, 0, Width, Height), Theme.SF(10)))
            {
                Region = new Region(path);
            }

            BeginInvoke((Action)delegate { _ready = true; });
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_ready)
            {
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(graphics, bounds, Theme.SF(10), Theme.Surface, Theme.BorderStrong, 1f);
        }

        public void ShowBelow(Control anchor)
        {
            Point origin = anchor.PointToScreen(new Point(0, anchor.Height + Theme.S(4)));
            Rectangle screen = Screen.FromControl(anchor).WorkingArea;

            int x = Math.Min(origin.X, screen.Right - Width - Theme.S(8));
            int y = origin.Y + Height > screen.Bottom
                ? anchor.PointToScreen(new Point(0, -Height - Theme.S(4))).Y
                : origin.Y;

            Location = new Point(Math.Max(screen.Left + Theme.S(4), x), Math.Max(screen.Top + Theme.S(4), y));
            Show();
            Activate();
        }
    }

    /// <summary>圆角日期框，点击弹出月历。</summary>
    internal sealed class UiDateField : Control
    {
        private DateTime _value = DateTime.Today;
        private bool _hover;
        private CalendarPopup _popup;

        public UiDateField()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(36);
            Cursor = Cursors.Hand;
        }

        public event EventHandler ValueChanged;

        public DateTime Value
        {
            get { return _value.Date; }
            set
            {
                _value = value.Date;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(
                graphics,
                bounds,
                Theme.SF(8),
                Theme.SurfaceMuted,
                _hover ? Theme.Brand : Theme.Border,
                1f);

            TextRenderer.DrawText(
                graphics,
                _value.ToString("yyyy-MM-dd"),
                Theme.FontBody,
                new Rectangle(Theme.S(12), 0, Width - Theme.S(40), Height),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawChevron(graphics);
        }

        private void DrawChevron(Graphics graphics)
        {
            using (Pen pen = new Pen(Theme.TextFaint, Theme.SF(1.4f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = Width - Theme.S(18);
                int cy = Height / 2;
                graphics.DrawLine(pen, cx - Theme.S(4), cy - Theme.S(2), cx, cy + Theme.S(2));
                graphics.DrawLine(pen, cx, cy + Theme.S(2), cx + Theme.S(4), cy - Theme.S(2));
            }
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

            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.Close();
                _popup = null;
                return;
            }

            _popup = new CalendarPopup(_value);
            _popup.DateChosen += delegate(object sender, DateTime chosen)
            {
                _value = chosen.Date;
                Invalidate();

                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _popup.FormClosed += delegate { _popup = null; };
            _popup.ShowBelow(this);
        }
    }

    /// <summary>圆角时间框，点击弹出小时与分钟选择。</summary>
    internal sealed class UiTimeField : Control
    {
        private int _hour;
        private int _minute;
        private bool _hover;
        private TimePopup _popup;

        public UiTimeField()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(36);
            Cursor = Cursors.Hand;
        }

        public event EventHandler ValueChanged;

        public TimeSpan Value
        {
            get { return new TimeSpan(_hour, _minute, 0); }
            set
            {
                _hour = Math.Max(0, Math.Min(23, value.Hours));
                _minute = Math.Max(0, Math.Min(59, value.Minutes));
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(
                graphics,
                bounds,
                Theme.SF(8),
                Theme.SurfaceMuted,
                _hover ? Theme.Brand : Theme.Border,
                1f);

            TextRenderer.DrawText(
                graphics,
                _hour.ToString("00") + ":" + _minute.ToString("00"),
                Theme.FontBody,
                new Rectangle(Theme.S(12), 0, Width - Theme.S(40), Height),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using (Pen pen = new Pen(Theme.TextFaint, Theme.SF(1.4f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = Width - Theme.S(18);
                int cy = Height / 2;
                graphics.DrawLine(pen, cx - Theme.S(4), cy - Theme.S(2), cx, cy + Theme.S(2));
                graphics.DrawLine(pen, cx, cy + Theme.S(2), cx + Theme.S(4), cy - Theme.S(2));
            }
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

            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.Close();
                _popup = null;
                return;
            }

            _popup = new TimePopup(_hour, _minute);
            _popup.TimeChosen += delegate(object sender, TimeSpan chosen)
            {
                _hour = chosen.Hours;
                _minute = chosen.Minutes;
                Invalidate();

                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _popup.FormClosed += delegate { _popup = null; };
            _popup.ShowBelow(this);
        }
    }

    /// <summary>月历弹层。</summary>
    internal sealed class CalendarPopup : PopupPanel
    {
        private DateTime _month;
        private DateTime _selected;
        private readonly Rectangle _prevRect;
        private readonly Rectangle _nextRect;
        private readonly int _gridTop;
        private int _hoverCell = -1;
        private int _hoverArrow; // 0 无，1 上一月，2 下一月

        public CalendarPopup(DateTime selected)
        {
            _selected = selected.Date;
            _month = new DateTime(selected.Year, selected.Month, 1);
            ClientSize = new Size(Theme.S(272), Theme.S(306));

            _prevRect = new Rectangle(Theme.S(14), Theme.S(14), Theme.S(24), Theme.S(24));
            _nextRect = new Rectangle(ClientSize.Width - Theme.S(38), Theme.S(14), Theme.S(24), Theme.S(24));
            _gridTop = Theme.S(74);
            Cursor = Cursors.Hand;
        }

        public event EventHandler<DateTime> DateChosen;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;

            string title = _month.ToString("yyyy 年 M 月");
            TextRenderer.DrawText(
                graphics,
                title,
                Theme.FontTitle,
                new Rectangle(Theme.S(44), Theme.S(10), ClientSize.Width - Theme.S(88), Theme.S(30)),
                Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (_hoverArrow == 1)
            {
                UiDraw.FillRoundedRect(graphics, _prevRect, Theme.SF(7), Theme.PaperMuted);
            }
            else if (_hoverArrow == 2)
            {
                UiDraw.FillRoundedRect(graphics, _nextRect, Theme.SF(7), Theme.PaperMuted);
            }

            DrawArrow(graphics, _prevRect, false);
            DrawArrow(graphics, _nextRect, true);

            string[] week = { "日", "一", "二", "三", "四", "五", "六" };
            int cellWidth = (ClientSize.Width - Theme.S(20)) / 7;
            for (int i = 0; i < 7; i++)
            {
                TextRenderer.DrawText(
                    graphics,
                    week[i],
                    Theme.FontTag,
                    new Rectangle(Theme.S(10) + i * cellWidth, Theme.S(52), cellWidth, Theme.S(20)),
                    Theme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            DateTime first = _month;
            int offset = (int)first.DayOfWeek;
            for (int i = 0; i < 42; i++)
            {
                DateTime day = first.AddDays(i - offset);
                Rectangle cell = CellRect(i, cellWidth);
                bool inMonth = day.Month == _month.Month;
                bool isSelected = day.Date == _selected.Date;
                bool isToday = day.Date == DateTime.Today;

                if (isSelected)
                {
                    UiDraw.FillRoundedRect(graphics, cell, Theme.SF(7), Theme.Brand);
                }
                else if (isToday)
                {
                    UiDraw.FillRoundedRect(graphics, cell, Theme.SF(7), Theme.BrandSoft);
                }
                else if (_hoverCell == i && inMonth)
                {
                    UiDraw.FillRoundedRect(graphics, cell, Theme.SF(7), Theme.PaperMuted);
                }

                Color fore = isSelected ? Theme.BrandOn : (inMonth ? Theme.Text : Theme.TextFaint);
                if (_hoverCell == i && inMonth && !isSelected)
                {
                    fore = Theme.Brand;
                }

                TextRenderer.DrawText(
                    graphics,
                    day.Day.ToString(),
                    Theme.FontMeta,
                    cell,
                    fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private Rectangle CellRect(int index, int cellWidth)
        {
            int row = index / 7;
            int column = index % 7;
            return new Rectangle(
                Theme.S(10) + column * cellWidth + Theme.S(2),
                _gridTop + row * Theme.S(34) + Theme.S(2),
                cellWidth - Theme.S(4),
                Theme.S(30));
        }

        private void DrawArrow(Graphics graphics, Rectangle bounds, bool right)
        {
            using (Pen pen = new Pen(Theme.TextMuted, Theme.SF(1.6f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = bounds.X + bounds.Width / 2;
                int cy = bounds.Y + bounds.Height / 2;
                int dx = right ? Theme.S(3) : -Theme.S(3);
                graphics.DrawLine(pen, cx - dx, cy - Theme.S(4), cx + dx, cy);
                graphics.DrawLine(pen, cx + dx, cy, cx - dx, cy + Theme.S(4));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverCell != -1 || _hoverArrow != 0)
            {
                _hoverCell = -1;
                _hoverArrow = 0;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        private void UpdateHover(Point location)
        {
            int arrow = _prevRect.Contains(location) ? 1 : (_nextRect.Contains(location) ? 2 : 0);
            int cell = -1;
            int cellWidth = (ClientSize.Width - Theme.S(20)) / 7;
            for (int i = 0; i < 42; i++)
            {
                if (CellRect(i, cellWidth).Contains(location))
                {
                    cell = i;
                    break;
                }
            }

            if (arrow != _hoverArrow || cell != _hoverCell)
            {
                _hoverArrow = arrow;
                _hoverCell = cell;
                Cursor = (arrow != 0 || cell != -1) ? Cursors.Hand : Cursors.Default;
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

            if (_prevRect.Contains(e.Location))
            {
                _month = _month.AddMonths(-1);
                Invalidate();
                return;
            }

            if (_nextRect.Contains(e.Location))
            {
                _month = _month.AddMonths(1);
                Invalidate();
                return;
            }

            int cellWidth = (ClientSize.Width - Theme.S(20)) / 7;
            for (int i = 0; i < 42; i++)
            {
                if (!CellRect(i, cellWidth).Contains(e.Location))
                {
                    continue;
                }

                int offset = (int)_month.DayOfWeek;
                DateTime day = _month.AddDays(i - offset);
                _selected = day.Date;

                EventHandler<DateTime> handler = DateChosen;
                if (handler != null)
                {
                    handler(this, day.Date);
                }

                Close();
                return;
            }
        }
    }

    /// <summary>时间弹层：小时与分钟可直接输入，附整点快选与微调按钮。</summary>
    internal sealed class TimePopup : PopupPanel
    {
        private readonly UiTextField _hourField;
        private readonly UiTextField _minuteField;
        private readonly Rectangle _hourUpRect;
        private readonly Rectangle _hourDownRect;
        private readonly Rectangle _minuteUpRect;
        private readonly Rectangle _minuteDownRect;
        private int _hour;
        private int _minute;
        private int _hoverStepper; // 0 无，1 时+，2 时-，3 分+，4 分-
        private int _hoverCell = -1;

        public TimePopup(int hour, int minute)
        {
            _hour = Clamp(hour, 0, 23);
            _minute = Clamp(minute, 0, 59);
            ClientSize = new Size(Theme.S(300), Theme.S(306));
            Cursor = Cursors.Hand;

            _hourUpRect = new Rectangle(Theme.S(114), Theme.S(44), Theme.S(18), Theme.S(17));
            _hourDownRect = new Rectangle(Theme.S(114), Theme.S(63), Theme.S(18), Theme.S(17));
            _minuteUpRect = new Rectangle(Theme.S(248), Theme.S(44), Theme.S(18), Theme.S(17));
            _minuteDownRect = new Rectangle(Theme.S(248), Theme.S(63), Theme.S(18), Theme.S(17));

            _hourField = new UiTextField();
            _hourField.SetBounds(Theme.S(34), Theme.S(44), Theme.S(76), Theme.S(36));
            _hourField.NumericOnly = true;
            _hourField.MaxLength = 2;
            _hourField.InputAlign = HorizontalAlignment.Center;
            _hourField.Text = _hour.ToString("00");
            _hourField.TextChangedEvent += delegate { ReadFields(false); };
            Controls.Add(_hourField);

            _minuteField = new UiTextField();
            _minuteField.SetBounds(Theme.S(168), Theme.S(44), Theme.S(76), Theme.S(36));
            _minuteField.NumericOnly = true;
            _minuteField.MaxLength = 2;
            _minuteField.InputAlign = HorizontalAlignment.Center;
            _minuteField.Text = _minute.ToString("00");
            _minuteField.TextChangedEvent += delegate { ReadFields(false); };
            Controls.Add(_minuteField);

            UiButton confirm = new UiButton();
            confirm.Primary = true;
            confirm.Text = "确定";
            confirm.SetBounds(Theme.S(210), Theme.S(258), Theme.S(76), Theme.S(34));
            confirm.Click += delegate { Commit(); };
            Controls.Add(confirm);
            AcceptButton = confirm;
        }

        public event EventHandler<TimeSpan> TimeChosen;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke((Action)delegate { _hourField.FocusInput(); });
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

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private Rectangle HourCell(int index)
        {
            int column = index % 6;
            int row = index / 6;
            return new Rectangle(Theme.S(14) + column * Theme.S(46), Theme.S(114) + row * Theme.S(32), Theme.S(42), Theme.S(28));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;

            TextRenderer.DrawText(
                graphics,
                "选择时间",
                Theme.FontSection,
                new Rectangle(Theme.S(14), Theme.S(10), Theme.S(120), Theme.S(24)),
                Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                graphics,
                _hour.ToString("00") + " : " + _minute.ToString("00"),
                Theme.FontTitle,
                new Rectangle(Theme.S(140), Theme.S(8), Theme.S(146), Theme.S(26)),
                Theme.Brand,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(
                graphics,
                ":",
                Theme.FontTitle,
                new Rectangle(Theme.S(138), Theme.S(44), Theme.S(24), Theme.S(36)),
                Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawStepper(graphics, _hourUpRect, true, _hoverStepper == 1);
            DrawStepper(graphics, _hourDownRect, false, _hoverStepper == 2);
            DrawStepper(graphics, _minuteUpRect, true, _hoverStepper == 3);
            DrawStepper(graphics, _minuteDownRect, false, _hoverStepper == 4);

            TextRenderer.DrawText(
                graphics,
                "快速选时",
                Theme.FontTag,
                new Rectangle(Theme.S(14), Theme.S(92), Theme.S(120), Theme.S(18)),
                Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            for (int i = 0; i < 24; i++)
            {
                Rectangle cell = HourCell(i);
                bool selected = i == _hour;
                bool hover = _hoverCell == i;

                if (selected)
                {
                    UiDraw.FillRoundedRect(graphics, cell, Theme.SF(6), Theme.Brand);
                }
                else if (hover)
                {
                    UiDraw.FillRoundedRect(graphics, cell, Theme.SF(6), Theme.PaperMuted);
                }

                TextRenderer.DrawText(
                    graphics,
                    i.ToString("00"),
                    Theme.FontMeta,
                    cell,
                    selected ? Theme.BrandOn : (hover ? Theme.Brand : Theme.TextMuted),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using (Pen line = new Pen(Theme.Border))
            {
                graphics.DrawLine(line, Theme.S(14), Theme.S(250), ClientSize.Width - Theme.S(14), Theme.S(250));
            }

            TextRenderer.DrawText(
                graphics,
                "回车确定 · Esc 取消",
                Theme.FontTag,
                new Rectangle(Theme.S(14), Theme.S(263), Theme.S(160), Theme.S(24)),
                Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawStepper(Graphics graphics, Rectangle bounds, bool up, bool hover)
        {
            if (hover)
            {
                UiDraw.FillRoundedRect(graphics, bounds, Theme.SF(4), Theme.PaperMuted);
            }

            using (Pen pen = new Pen(Theme.TextMuted, Theme.SF(1.3f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = bounds.X + bounds.Width / 2;
                int cy = bounds.Y + bounds.Height / 2;
                int dx = Theme.S(3);
                if (up)
                {
                    graphics.DrawLine(pen, cx - dx, cy + 1, cx, cy - 2);
                    graphics.DrawLine(pen, cx, cy - 2, cx + dx, cy + 1);
                }
                else
                {
                    graphics.DrawLine(pen, cx - dx, cy - 1, cx, cy + 2);
                    graphics.DrawLine(pen, cx, cy + 2, cx + dx, cy - 1);
                }
            }
        }

        /// <summary>从输入框读取数值；normalize 时把非法内容写回规范格式。</summary>
        private void ReadFields(bool normalize)
        {
            int parsed;
            if (int.TryParse(_hourField.Value, out parsed))
            {
                _hour = Clamp(parsed, 0, 23);
            }

            if (int.TryParse(_minuteField.Value, out parsed))
            {
                _minute = Clamp(parsed, 0, 59);
            }

            if (normalize)
            {
                _hourField.Text = _hour.ToString("00");
                _minuteField.Text = _minute.ToString("00");
            }

            Invalidate();
        }

        private void Bump(bool hour, int delta)
        {
            if (hour)
            {
                _hour = (_hour + delta + 24) % 24;
                _hourField.Text = _hour.ToString("00");
            }
            else
            {
                _minute = (_minute + delta + 60) % 60;
                _minuteField.Text = _minute.ToString("00");
            }

            Invalidate();
        }

        private void Commit()
        {
            ReadFields(true);

            EventHandler<TimeSpan> handler = TimeChosen;
            if (handler != null)
            {
                handler(this, new TimeSpan(_hour, _minute, 0));
            }

            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int stepper = 0;
            if (_hourUpRect.Contains(e.Location)) stepper = 1;
            else if (_hourDownRect.Contains(e.Location)) stepper = 2;
            else if (_minuteUpRect.Contains(e.Location)) stepper = 3;
            else if (_minuteDownRect.Contains(e.Location)) stepper = 4;

            int cell = -1;
            for (int i = 0; i < 24; i++)
            {
                if (HourCell(i).Contains(e.Location))
                {
                    cell = i;
                    break;
                }
            }

            if (stepper != _hoverStepper || cell != _hoverCell)
            {
                _hoverStepper = stepper;
                _hoverCell = cell;
                Cursor = (stepper != 0 || cell != -1) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverStepper != 0 || _hoverCell != -1)
            {
                _hoverStepper = 0;
                _hoverCell = -1;
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

            if (_hourUpRect.Contains(e.Location)) { Bump(true, 1); return; }
            if (_hourDownRect.Contains(e.Location)) { Bump(true, -1); return; }
            if (_minuteUpRect.Contains(e.Location)) { Bump(false, 1); return; }
            if (_minuteDownRect.Contains(e.Location)) { Bump(false, -1); return; }

            for (int i = 0; i < 24; i++)
            {
                if (HourCell(i).Contains(e.Location))
                {
                    _hour = i;
                    _hourField.Text = i.ToString("00");
                    Invalidate();
                    return;
                }
            }
        }
    }

    /// <summary>自绘滑块，用于遮罩暗度这类 0-100 的调节。</summary>
    internal sealed class UiSlider : Control
    {
        private int _value;
        private bool _dragging;

        public UiSlider()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            Height = Theme.S(26);
            Cursor = Cursors.Hand;
            Minimum = 0;
            Maximum = 100;
            Value = 0;
        }

        public event EventHandler ValueChanged;

        public int Minimum { get; set; }

        public int Maximum { get; set; }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (_value == clamped)
                {
                    return;
                }

                _value = clamped;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int trackHeight = Theme.S(4);
            int knob = Theme.S(15);
            int left = knob / 2;
            int width = Math.Max(1, Width - knob);
            int centerY = Height / 2;

            Rectangle track = new Rectangle(left, centerY - trackHeight / 2, width, trackHeight);
            UiDraw.FillRoundedRect(graphics, track, trackHeight / 2f, Theme.Border);

            int filled = (int)Math.Round(width * (_value - Minimum) / (float)Math.Max(1, Maximum - Minimum));
            if (filled > 0)
            {
                UiDraw.FillRoundedRect(
                    graphics,
                    new Rectangle(left, centerY - trackHeight / 2, filled, trackHeight),
                    trackHeight / 2f,
                    Theme.Brand);
            }

            Rectangle knobRect = new Rectangle(left + filled - knob / 2, centerY - knob / 2, knob, knob);
            UiDraw.DrawRoundedRect(graphics, knobRect, knob / 2f, Theme.Surface, Theme.Brand, 2f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = true;
            UpdateFromPoint(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                UpdateFromPoint(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        private void UpdateFromPoint(int x)
        {
            int knob = Theme.S(15);
            int left = knob / 2;
            int width = Math.Max(1, Width - knob);
            float ratio = (x - left) / (float)width;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int next = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);

            if (next == _value)
            {
                return;
            }

            _value = next;
            Invalidate();

            EventHandler handler = ValueChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
