using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>
    /// 提醒卡片的绘制与命中区域。轻提醒与全屏遮罩共用同一套外观，
    /// 区别只在于全屏模式不提供右上角关闭。
    /// </summary>
    internal sealed class ReminderCardView
    {
        private readonly TodoTask _task;
        private readonly bool _isLate;

        public ReminderCardView(TodoTask task, bool isLate, bool showClose)
        {
            _task = task;
            _isLate = isLate;
            ShowClose = showClose;

            Buttons.Add(new PillButton("已完成", "complete", true));
            Buttons.Add(new PillButton("延后 5 分钟", "snooze5", false));
            Buttons.Add(new PillButton("延后 10 分钟", "snooze10", false));
            Buttons.Add(new PillButton("自定义", "custom", false));
        }

        public List<PillButton> Buttons { get; private set; } = new List<PillButton>();

        public bool ShowClose { get; private set; }

        public RectangleF CardBounds { get; private set; }

        public RectangleF CloseRect { get; private set; }

        public bool CloseHover { get; set; }

        public static SizeF GetPreferredSize()
        {
            return new SizeF(Theme.S(440), Theme.S(176));
        }

        /// <summary>按给定卡片矩形重新计算按钮与关闭区域。</summary>
        public void Layout(Graphics graphics, RectangleF cardBounds)
        {
            CardBounds = cardBounds;

            float padding = Theme.SF(20);
            float buttonHeight = Theme.SF(32);
            float buttonY = cardBounds.Bottom - padding - buttonHeight;
            float gap = Theme.SF(8);

            float x = cardBounds.X + padding;
            foreach (PillButton button in Buttons)
            {
                float width = PillButton.MeasureWidth(graphics, button.Text);
                button.Bounds = new RectangleF(x, buttonY, width, buttonHeight);
                x += width + gap;
            }

            CloseRect = new RectangleF(
                cardBounds.Right - Theme.SF(36),
                cardBounds.Y + Theme.SF(12),
                Theme.SF(24),
                Theme.SF(24));
        }

        public void Draw(Graphics graphics, bool withShadow)
        {
            float radius = Theme.SF(14);

            if (withShadow)
            {
                UiDraw.DrawSoftShadow(graphics, CardBounds, radius, 7, Theme.S(12), 38);
            }

            UiDraw.DrawRoundedRect(graphics, CardBounds, radius, Theme.Surface, Theme.Border, 1f);

            // 左侧品牌竖线：渐变质感，裁到圆角内部。
            using (GraphicsPath clip = UiDraw.RoundedRect(CardBounds, radius))
            {
                Region previous = graphics.Clip;
                graphics.SetClip(clip);
                Color accentTop = _isLate ? Theme.Amber : Theme.BrandShine;
                Color accentBottom = _isLate ? Theme.Amber : Theme.Brand;
                UiDraw.FillVerticalGradient(
                    graphics,
                    new RectangleF(CardBounds.X, CardBounds.Y, Theme.SF(3.5f), CardBounds.Height),
                    Theme.SF(1.5f),
                    accentTop,
                    accentBottom);
                graphics.Clip = previous;
            }

            float padding = Theme.SF(20);
            float left = CardBounds.X + padding;
            float top = CardBounds.Y + Theme.SF(16);
            float contentWidth = CardBounds.Width - padding * 2 - Theme.SF(26);

            UiDraw.DrawText(
                graphics,
                _task.Title,
                Theme.FontTitle,
                Theme.Text,
                new RectangleF(left, top, contentWidth, Theme.SF(26)),
                StringAlignment.Near);

            top += Theme.SF(30);

            // 时间行放进柔和胶囊里，强化视觉层次。
            Color timeColor = _isLate ? Theme.Amber : Theme.Brand;
            Color chipFill = _isLate ? Theme.AmberSoft : Theme.BrandSoft;
            string dueText = ReminderText.DescribeDue(_task, _isLate);
            float chipHeight = Theme.SF(22);
            float chipWidth = Theme.SF(26) + TextWidth(graphics, dueText, Theme.FontSmall) + Theme.SF(12);

            UiDraw.FillRoundedRect(
                graphics,
                new RectangleF(left, top, chipWidth, chipHeight),
                chipHeight / 2f,
                chipFill);

            using (SolidBrush dot = new SolidBrush(timeColor))
            {
                graphics.FillEllipse(dot, left + Theme.SF(9), top + chipHeight / 2f - Theme.SF(2.5f), Theme.SF(5), Theme.SF(5));
            }

            UiDraw.DrawText(
                graphics,
                dueText,
                Theme.FontSmall,
                timeColor,
                new RectangleF(left + Theme.SF(19), top, chipWidth - Theme.SF(19), chipHeight),
                StringAlignment.Near);

            top += Theme.SF(28);

            if (!string.IsNullOrWhiteSpace(_task.Note))
            {
                UiDraw.DrawText(
                    graphics,
                    _task.Note,
                    Theme.FontSmall,
                    Theme.TextMuted,
                    new RectangleF(left, top, CardBounds.Width - padding * 2, Theme.SF(20)),
                    StringAlignment.Near);
            }

            if (ShowClose)
            {
                DrawCloseButton(graphics);
            }

            foreach (PillButton button in Buttons)
            {
                button.Draw(graphics);
            }
        }

        private static float TextWidth(Graphics graphics, string text, Font font)
        {
            SizeF size = graphics.MeasureString(text ?? string.Empty, font);
            return size.Width;
        }

        public bool HitTest(Point point, out string action)
        {
            action = null;

            if (ShowClose && CloseRect.Contains(point))
            {
                action = "close";
                return true;
            }

            foreach (PillButton button in Buttons)
            {
                if (button.Contains(point))
                {
                    action = button.Action;
                    return true;
                }
            }

            return false;
        }

        private void DrawCloseButton(Graphics graphics)
        {
            if (CloseHover)
            {
                UiDraw.FillRoundedRect(graphics, CloseRect, Theme.SF(11), Theme.PaperMuted);
            }

            float inset = Theme.SF(8);
            using (Pen pen = new Pen(CloseHover ? Theme.Text : Theme.TextFaint, Theme.SF(1.3f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLine(
                    pen,
                    CloseRect.Left + inset,
                    CloseRect.Top + inset,
                    CloseRect.Right - inset,
                    CloseRect.Bottom - inset);
                graphics.DrawLine(
                    pen,
                    CloseRect.Right - inset,
                    CloseRect.Top + inset,
                    CloseRect.Left + inset,
                    CloseRect.Bottom - inset);
            }
        }
    }
}
