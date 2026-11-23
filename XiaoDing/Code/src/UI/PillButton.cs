using System;
using System.Drawing;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>卡片上的胶囊按钮。纯数据 + 绘制 + 命中测试，不依赖控件树。</summary>
    internal sealed class PillButton
    {
        public PillButton(string text, string action, bool primary)
        {
            Text = text;
            Action = action;
            Primary = primary;
        }

        public string Text { get; private set; }
        public string Action { get; private set; }
        public bool Primary { get; private set; }
        public RectangleF Bounds { get; set; }
        public bool Hover { get; set; }
        public bool Pressed { get; set; }

        public bool Contains(Point point)
        {
            return Bounds.Contains(point);
        }

        public void Draw(Graphics graphics)
        {
            float radius = Bounds.Height / 2f;
            Color textColor;
            Color borderColor = Color.Transparent;
            float borderWidth = 0f;

            if (Primary)
            {
                textColor = Theme.BrandOn;
                if (Pressed)
                {
                    UiDraw.FillVerticalGradient(graphics, Bounds, radius, Theme.BrandDark, Theme.Brand);
                }
                else if (Hover)
                {
                    UiDraw.FillVerticalGradient(graphics, Bounds, radius, Theme.BrandHover, Theme.Brand);
                }
                else
                {
                    UiDraw.FillVerticalGradient(graphics, Bounds, radius, Theme.BrandShine, Theme.Brand);
                }
            }
            else
            {
                Color fill = Pressed ? Theme.BrandSoft : (Hover ? Theme.BrandSoft : Theme.Surface);
                textColor = Hover ? Theme.Brand : Theme.Text;
                borderColor = Hover ? Theme.Brand : Theme.BorderStrong;
                borderWidth = 1f;
                UiDraw.DrawRoundedRect(graphics, Bounds, radius, fill, borderColor, borderWidth);
            }

            UiDraw.DrawText(graphics, Text, Theme.FontButton, textColor, Bounds, StringAlignment.Center);
        }

        /// <summary>按文本宽度计算合适的按钮宽度。</summary>
        public static float MeasureWidth(Graphics graphics, string text)
        {
            SizeF size = graphics.MeasureString(text, Theme.FontButton);
            return (float)Math.Ceiling(size.Width) + Theme.SF(26f);
        }
    }
}
