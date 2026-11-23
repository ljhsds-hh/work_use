using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>
    /// 轻量卡片式对话框基类：无系统边框、圆角、可拖动、Esc 取消，
    /// 用于替代系统 MessageBox / 数字输入框这类原生弹窗。
    /// </summary>
    internal class UiCardDialog : Form
    {
        private bool _dragging;
        private Point _dragOrigin;

        protected UiCardDialog()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.FontBody;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        /// <summary>圆角半径（设计像素）。</summary>
        protected virtual int RadiusDip
        {
            get { return 14; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = UiDraw.RoundedRect(new RectangleF(0, 0, Width, Height), Theme.SF(RadiusDip)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Theme.TextHint;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UiDraw.DrawRoundedRect(graphics, bounds, Theme.SF(RadiusDip), Theme.Surface, Theme.BorderStrong, 1f);
            OnDrawContent(graphics);
        }

        /// <summary>子类在此绘制内容（坐标从 0,0 起）。</summary>
        protected virtual void OnDrawContent(Graphics graphics)
        {
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOrigin = Cursor.Position;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(Location.X + now.X - _dragOrigin.X, Location.Y + now.Y - _dragOrigin.Y);
                _dragOrigin = now;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // 无边框窗口下 Esc 也要能取消。
                OnCancelClicked(this, EventArgs.Empty);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected void OnCancelClicked(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
