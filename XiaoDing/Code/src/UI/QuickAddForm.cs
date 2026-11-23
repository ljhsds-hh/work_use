using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>全局快捷键唤起的快速输入条。回车保存，Esc 取消。</summary>
    internal sealed class QuickAddForm : Form
    {
        private readonly TextBox _input;
        private readonly Action<QuickParseResult> _onSubmit;
        private QuickParseResult _parsed;

        public QuickAddForm(Action<QuickParseResult> onSubmit)
        {
            _onSubmit = onSubmit;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.FontBody;
            KeyPreview = true;
            DoubleBuffered = true;
            ClientSize = new Size(Theme.S(620), Theme.S(150));

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.Font = new Font(Theme.FontBody.FontFamily, 13f, FontStyle.Regular);
            _input.BackColor = Theme.Surface;
            _input.ForeColor = Theme.Text;
            _input.SetBounds(Theme.S(66), Theme.S(28), Theme.S(526), Theme.S(30));
            _input.TextChanged += delegate { UpdateSummary(); };
            _input.KeyDown += OnInputKeyDown;
            Controls.Add(_input);

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                work.Left + (work.Width - Width) / 2,
                work.Top + work.Height / 4);

            using (GraphicsPath path = UiDraw.RoundedRect(new RectangleF(0, 0, Width, Height), Theme.SF(12)))
            {
                Region = new Region(path);
            }

            UpdateSummary();
        }

        public void FocusInput()
        {
            Activate();
            _input.Focus();
            _input.SelectAll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(graphics, bounds, Theme.SF(12), Theme.Surface, Theme.BorderStrong, 1f);

            Rectangle plus = new Rectangle(Theme.S(22), Theme.S(26), Theme.S(30), Theme.S(30));
            UiDraw.FillVerticalGradient(graphics, plus, Theme.SF(9), Theme.BrandShine, Theme.Brand);

            using (Pen pen = new Pen(Theme.BrandOn, Theme.SF(1.8f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = plus.X + plus.Width / 2;
                int cy = plus.Y + plus.Height / 2;
                int arm = Theme.S(6);
                graphics.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                graphics.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }

            int chipY = Theme.S(78);
            bool empty = string.IsNullOrWhiteSpace(_input.Text);

            if (empty || _parsed == null)
            {
                TextRenderer.DrawText(
                    graphics,
                    "例如：明天9点 交周报 #工作",
                    Theme.FontSmall,
                    new Rectangle(Theme.S(22), chipY, Theme.S(360), Theme.S(24)),
                    Theme.TextFaint,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                int x = Theme.S(22);
                x = DrawChip(graphics, x, chipY, _parsed.Summary, Theme.BrandSoft, Theme.Brand);

                if (!string.IsNullOrWhiteSpace(_parsed.Group))
                {
                    x = DrawChip(graphics, x, chipY, "分组 · " + _parsed.Group, Theme.PaperMuted, Theme.TextMuted);
                }

                if (!string.IsNullOrWhiteSpace(_parsed.Title))
                {
                    x = DrawChip(graphics, x, chipY, "标题 · " + _parsed.Title, Theme.PaperMuted, Theme.TextMuted);
                }
            }

            TextRenderer.DrawText(
                graphics,
                "回车保存 · Esc 取消",
                Theme.FontTag,
                new Rectangle(Width - Theme.S(160), chipY, Theme.S(138), Theme.S(24)),
                Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private int DrawChip(Graphics graphics, int x, int y, string text, Color fill, Color fore)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return x;
            }

            int height = Theme.S(24);
            Size size = TextRenderer.MeasureText(graphics, text, Theme.FontTag, new Size(int.MaxValue, height), TextFormatFlags.NoPadding);
            int width = size.Width + Theme.S(18);
            Rectangle bounds = new Rectangle(x, y, width, height);

            UiDraw.FillRoundedRect(graphics, bounds, Theme.SF(12), fill);
            TextRenderer.DrawText(
                graphics,
                text,
                Theme.FontTag,
                bounds,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            return x + width + Theme.S(8);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }

            base.OnKeyDown(e);
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Close();
            }
        }

        private void UpdateSummary()
        {
            string raw = _input.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                _parsed = null;
            }
            else
            {
                _parsed = QuickParse.Parse(raw, DateTime.Now);
            }

            Invalidate();
        }

        private void Submit()
        {
            string raw = _input.Text.Trim();
            if (raw.Length == 0)
            {
                return;
            }

            QuickParseResult parsed = QuickParse.Parse(raw, DateTime.Now);
            Action<QuickParseResult> callback = _onSubmit;
            Close();

            if (callback != null)
            {
                callback(parsed);
            }
        }
    }
}
