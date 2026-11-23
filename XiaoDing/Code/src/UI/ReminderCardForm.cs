using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>右下角轻提醒卡片。不抢焦点，可继续当前工作。</summary>
    internal sealed class ReminderCardForm : LayeredForm
    {
        private readonly ReminderCardView _view;
        private readonly Action<ReminderResult> _onResult;
        private readonly Timer _animation;

        private RectangleF _cardRect;
        private Point _targetLocation;
        private int _alpha;
        private int _offsetY;
        private bool _resultSent;
        private bool _allowClose;

        public ReminderCardForm(TodoTask task, bool isLate, Action<ReminderResult> onResult)
        {
            _view = new ReminderCardView(task, isLate, true);
            _onResult = onResult;

            Text = "小叮提醒";
            BuildSurface();

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int x = work.Right - Surface.Width - Theme.S(10);
            int y = work.Bottom - Surface.Height - Theme.S(10);
            _targetLocation = new Point(x, y);
            _offsetY = Theme.S(12);

            _animation = new Timer();
            _animation.Interval = 15;
            _animation.Tick += OnAnimationTick;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void ShowCard()
        {
            Location = new Point(_targetLocation.X, _targetLocation.Y + _offsetY);
            Show();
            Present(Location, 0);
            _animation.Start();
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

            bool closeHover = _view.ShowClose && _view.CloseRect.Contains(e.Location);
            if (closeHover != _view.CloseHover)
            {
                _view.CloseHover = closeHover;
                dirty = true;
            }

            if (dirty)
            {
                Rebuild();
            }

            Cursor = HitAny(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            bool dirty = false;

            foreach (PillButton button in _view.Buttons)
            {
                if (button.Hover || button.Pressed)
                {
                    button.Hover = false;
                    button.Pressed = false;
                    dirty = true;
                }
            }

            if (_view.CloseHover)
            {
                _view.CloseHover = false;
                dirty = true;
            }

            if (dirty)
            {
                Rebuild();
            }

            base.OnMouseLeave(e);
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
                        Rebuild();
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
                string action;
                bool pressedSomething = false;

                foreach (PillButton button in _view.Buttons)
                {
                    if (button.Pressed)
                    {
                        pressedSomething = true;
                        bool inside = button.Contains(e.Location);
                        button.Pressed = false;

                        if (inside)
                        {
                            HandleAction(button.Action);
                        }
                        else
                        {
                            Rebuild();
                        }

                        break;
                    }
                }

                if (!pressedSomething
                    && _view.HitTest(e.Location, out action)
                    && action == "close")
                {
                    // 关闭按钮视为误操作：按默认延后 5 分钟处理，任务未完成前持续提醒。
                    SendResult(ReminderResult.Snooze(5));
                }
            }

            base.OnMouseUp(e);
        }

        /// <summary>由程序内部关闭（删除/编辑/退出），不触发「关窗=延后」语义。</summary>
        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 用户用 Alt+F4 等方式关窗按误操作处理：等同「延后 5 分钟」，
            // 与关闭按钮语义一致，任务没点「已完成」就还会再提醒。
            if (!_allowClose && !_resultSent && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SendResult(ReminderResult.Snooze(5));
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
                        Rebuild();
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
            _animation.Stop();

            Action<ReminderResult> callback = _onResult;
            Close();

            if (callback != null)
            {
                callback(result);
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            _alpha += 26;
            if (_alpha >= 255)
            {
                _alpha = 255;
                _animation.Stop();
            }

            float progress = _alpha / 255f;
            int y = _targetLocation.Y + (int)Math.Round(_offsetY * (1f - progress));
            Location = new Point(_targetLocation.X, y);
            Present(Location, (byte)_alpha);
        }

        private bool HitAny(Point point)
        {
            string action;
            return _view.HitTest(point, out action);
        }

        private void Rebuild()
        {
            DrawSurface();
            Present(Location, (byte)(_alpha > 0 ? _alpha : 255));
        }

        private void BuildSurface()
        {
            int margin = Theme.S(16);
            SizeF card = ReminderCardView.GetPreferredSize();
            EnsureSurface((int)card.Width + margin * 2, (int)card.Height + margin * 2);
            _cardRect = new RectangleF(margin, margin, card.Width, card.Height);
            DrawSurface();
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

                _view.Layout(graphics, _cardRect);
                _view.Draw(graphics, true);
            }
        }
    }
}
