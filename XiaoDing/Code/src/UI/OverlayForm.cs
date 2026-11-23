using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>
    /// 全屏强提醒：整屏蒙布（或自定义背景图），提示卡片仍固定在右下角，
    /// 只保留完成与延后两条出口。
    /// </summary>
    internal sealed class OverlayForm : LayeredForm
    {
        private static Image _background;
        private static DateTime _backgroundStamp = DateTime.MinValue;

        private readonly ReminderCardView _view;
        private readonly Action<ReminderResult> _onResult;
        private readonly Timer _animation;
        private readonly int _dim;

        private RectangleF _cardRect;
        private int _alpha;
        private bool _allowClose;
        private bool _resultSent;

        public OverlayForm(TodoTask task, bool isLate, int dim, Action<ReminderResult> onResult)
        {
            _view = new ReminderCardView(task, isLate, false);
            _onResult = onResult;
            _dim = Math.Max(0, Math.Min(255, dim));

            Text = "小叮强提醒";
            TopMost = true;

            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            EnsureSurface(bounds.Width, bounds.Height);
            BuildCardLayout(bounds);
            DrawSurface();

            _animation = new Timer();
            _animation.Interval = 15;
            _animation.Tick += OnAnimationTick;
        }

        public void ShowOverlay()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Location = bounds.Location;
            Show();
            Present(bounds.Location, 0);
            Activate();
            _animation.Start();
        }

        /// <summary>由程序内部关闭（退出或任务被删除），绕过用户关闭拦截。</summary>
        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool dirty = false;
            foreach (PillButton button in _view.Buttons)
            {
                bool hover = button.Contains(e.Location);
                if (hover != button.Hover)
                {
                    button.Hover = hover;
                    dirty = true;
                }
            }

            if (dirty)
            {
                Redraw();
            }

            Cursor = HitAny(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                foreach (PillButton button in _view.Buttons)
                {
                    if (button.Contains(e.Location))
                    {
                        button.Pressed = true;
                        Redraw();
                        break;
                    }
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                foreach (PillButton button in _view.Buttons)
                {
                    if (button.Pressed)
                    {
                        bool inside = button.Contains(e.Location);
                        button.Pressed = false;

                        if (inside)
                        {
                            HandleAction(button.Action);
                        }
                        else
                        {
                            Redraw();
                        }

                        break;
                    }
                }
            }

            base.OnMouseUp(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                // 系统关机、注销或任务管理器结束进程时必须放行，拦截会拖住关机。
                if (e.CloseReason == CloseReason.WindowsShutDown
                    || e.CloseReason == CloseReason.TaskManagerClosing
                    || e.CloseReason == CloseReason.ApplicationExitCall)
                {
                    _animation.Stop();
                    base.OnFormClosing(e);
                    return;
                }

                e.Cancel = true;
                return;
            }

            _animation.Stop();
            base.OnFormClosing(e);
        }

        private void HandleAction(string action)
        {
            switch (action)
            {
                case "complete":
                    SendResult(ReminderResult.Complete());
                    break;

                case "snooze5":
                    SendResult(ReminderResult.Snooze(5));
                    break;

                case "snooze10":
                    SendResult(ReminderResult.Snooze(10));
                    break;

                case "custom":
                {
                    int minutes;
                    if (PromptDialog.TryAskMinutes(this, "延后提醒", "多少分钟后再提醒？", 15, out minutes))
                    {
                        SendResult(ReminderResult.Snooze(minutes));
                    }
                    else
                    {
                        Redraw();
                    }

                    break;
                }
            }
        }

        private void SendResult(ReminderResult result)
        {
            if (_resultSent)
            {
                return;
            }

            _resultSent = true;
            _allowClose = true;

            Action<ReminderResult> callback = _onResult;
            Close();

            if (callback != null)
            {
                callback(result);
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            _alpha += 30;
            if (_alpha >= 255)
            {
                _alpha = 255;
                _animation.Stop();
            }

            Present(Location, (byte)_alpha);
        }

        private void Redraw()
        {
            DrawSurface();
            Present(Location, (byte)(_alpha > 0 ? _alpha : 255));
        }

        private bool HitAny(Point point)
        {
            string action;
            return _view.HitTest(point, out action);
        }

        private void BuildCardLayout(Rectangle screenBounds)
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            SizeF card = ReminderCardView.GetPreferredSize();
            float margin = Theme.SF(26);

            _cardRect = new RectangleF(
                work.Right - screenBounds.X - margin - card.Width,
                work.Bottom - screenBounds.Y - margin - card.Height,
                card.Width,
                card.Height);
        }

        private void DrawSurface()
        {
            if (Surface == null)
            {
                return;
            }

            using (Graphics graphics = Graphics.FromImage(Surface))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = Theme.TextHint;

                Rectangle area = new Rectangle(0, 0, Surface.Width, Surface.Height);
                Image background = LoadBackground();

                if (background != null)
                {
                    DrawCover(graphics, background, area);
                }

                if (_dim > 0)
                {
                    using (SolidBrush dim = new SolidBrush(Color.FromArgb(_dim, Theme.OverlayDim)))
                    {
                        graphics.FillRectangle(dim, area);
                    }
                }

                DrawVignette(graphics, area);
                DrawOverlayHint(graphics, area);

                _view.Layout(graphics, _cardRect);
                _view.Draw(graphics, true);
            }
        }

        /// <summary>上下渐晕：底部渐深托住提示卡片，顶部轻微压暗托住标题行。</summary>
        private static void DrawVignette(Graphics graphics, Rectangle area)
        {
            if (graphics == null)
            {
                return;
            }

            float bottomTop = area.Y + area.Height * 0.42f;
            RectangleF bottom = new RectangleF(area.X, bottomTop, area.Width, area.Bottom - bottomTop);
            using (LinearGradientBrush brush = new LinearGradientBrush(
                bottom,
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(112, 0, 0, 0),
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(brush, bottom);
            }

            RectangleF top = new RectangleF(area.X, area.Y, area.Width, area.Height * 0.16f);
            using (LinearGradientBrush brush = new LinearGradientBrush(
                top,
                Color.FromArgb(64, 0, 0, 0),
                Color.FromArgb(0, 0, 0, 0),
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(brush, top);
            }
        }

        private static void DrawOverlayHint(Graphics graphics, Rectangle area)
        {
            if (graphics == null)
            {
                return;
            }

            string text = "全屏提醒 · 处理后才能继续";

            using (Font font = new Font(Theme.FontSmall.FontFamily, 8.5f, FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF measured = graphics.MeasureString(text, font);
                float chipHeight = Theme.SF(30);
                float chipWidth = measured.Width + Theme.SF(34);
                RectangleF chip = new RectangleF(
                    area.X + (area.Width - chipWidth) / 2f,
                    Theme.SF(22),
                    chipWidth,
                    chipHeight);

                UiDraw.FillRoundedRect(graphics, chip, chipHeight / 2f, Color.FromArgb(42, 255, 255, 255));

                using (SolidBrush dot = new SolidBrush(Theme.Brand))
                {
                    graphics.FillEllipse(
                        dot,
                        chip.X + Theme.SF(13),
                        chip.Y + chipHeight / 2f - Theme.SF(2.5f),
                        Theme.SF(5),
                        Theme.SF(5));
                }

                using (StringFormat format = new StringFormat())
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(196, 255, 255, 255)))
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    graphics.DrawString(
                        text,
                        font,
                        brush,
                        new RectangleF(chip.X + Theme.SF(23), chip.Y, chip.Width - Theme.SF(30), chip.Height),
                        format);
                }
            }
        }

        private static void DrawCover(Graphics graphics, Image image, Rectangle target)
        {
            float scale = Math.Max(
                (float)target.Width / image.Width,
                (float)target.Height / image.Height);

            int width = (int)Math.Ceiling(image.Width * scale);
            int height = (int)Math.Ceiling(image.Height * scale);
            int x = target.X + (target.Width - width) / 2;
            int y = target.Y + (target.Height - height) / 2;

            graphics.DrawImage(image, new Rectangle(x, y, width, height));
        }

        private static Image LoadBackground()
        {
            try
            {
                string path = AppPaths.OverlayImagePath;
                if (!File.Exists(path))
                {
                    if (_background != null)
                    {
                        _background.Dispose();
                        _background = null;
                    }

                    _backgroundStamp = DateTime.MinValue;
                    return null;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (_background != null && stamp == _backgroundStamp)
                {
                    return _background;
                }

                if (_background != null)
                {
                    _background.Dispose();
                    _background = null;
                }

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image loaded = Image.FromStream(stream))
                {
                    _background = new Bitmap(loaded);
                }

                _backgroundStamp = stamp;
                return _background;
            }
            catch (Exception ex)
            {
                AppLog.Error("Overlay.Background.Failed", ex);
                return null;
            }
        }
    }
}
