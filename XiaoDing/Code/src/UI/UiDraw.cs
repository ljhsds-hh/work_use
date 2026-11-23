using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace XiaoDing.UI
{
    /// <summary>圆角、文本、按钮等基础绘制工具。所有尺寸均以设计像素传入。</summary>
    internal static class UiDraw
    {
        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(0f, radius * 2f);
            if (diameter <= 0f)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            RectangleF arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180f, 90f);

            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270f, 90f);

            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0f, 90f);

            arc.X = bounds.X;
            path.AddArc(arc, 90f, 90f);

            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRect(Graphics graphics, RectangleF bounds, float radius, Color color)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRect(Graphics graphics, RectangleF bounds, float radius, Color fill, Color border, float borderWidth)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    graphics.FillPath(brush, path);
                }

                if (borderWidth > 0f)
                {
                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>纵向渐变圆角填充，用于主按钮、品牌块等需要质感的表面。</summary>
        public static void FillVerticalGradient(Graphics graphics, RectangleF bounds, float radius, Color top, Color bottom)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                return;
            }

            RectangleF area = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            using (GraphicsPath path = RoundedRect(area, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(area, top, bottom, LinearGradientMode.Vertical))
            {
                graphics.FillPath(brush, path);
            }
        }

        /// <summary>键盘焦点环：柔和品牌描边，不遮挡内容。</summary>
        public static void DrawFocusRing(Graphics graphics, RectangleF bounds, float radius, Color color)
        {
            RectangleF ring = RectangleF.Inflate(bounds, 2f, 2f);
            using (GraphicsPath path = RoundedRect(ring, radius + 2f))
            using (Pen pen = new Pen(Color.FromArgb(90, color), 1.6f))
            {
                graphics.DrawPath(pen, path);
            }
        }

        public static void DrawText(Graphics graphics, string text, Font font, Color color, RectangleF bounds, StringAlignment align)
        {
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;

                using (SolidBrush brush = new SolidBrush(color))
                {
                    graphics.DrawString(text ?? string.Empty, font, brush, bounds, format);
                }
            }
        }

        /// <summary>双层柔和阴影：外层大扩散定氛围，内层小扩散定厚度。</summary>
        public static void DrawSoftShadow(Graphics graphics, RectangleF cardBounds, float radius, int layers, int spread, int maxAlpha)
        {
            Color tint = Theme.ShadowTint;

            // 外层氛围阴影
            for (int i = layers; i >= 1; i--)
            {
                float grow = spread * i / (float)layers;
                RectangleF bounds = RectangleF.Inflate(cardBounds, grow, grow);
                int alpha = Math.Max(1, maxAlpha / (i + 1));
                using (GraphicsPath path = RoundedRect(bounds, radius + grow))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, tint)))
                {
                    graphics.FillPath(brush, path);
                }
            }

            // 内层贴边阴影，让卡片有“浮起来”的厚度
            RectangleF tight = RectangleF.Inflate(cardBounds, Theme.SF(1.5f), Theme.SF(1.5f));
            using (GraphicsPath path = RoundedRect(tight, radius + Theme.SF(1.5f)))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(2, maxAlpha / 5), tint)))
            {
                graphics.FillPath(brush, path);
            }
        }
    }
}
