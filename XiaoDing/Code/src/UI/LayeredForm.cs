using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>
    /// 基于 UpdateLayeredWindow 的分层窗口基类，支持逐像素透明、圆角与柔和阴影。
    /// 内容全部在 <see cref="Surface"/> 上绘制，不依赖子控件。
    /// </summary>
    internal class LayeredForm : Form
    {
        protected Bitmap Surface;

        public LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 分层窗口由位图直接呈现，无需系统背景绘制。
        }

        protected void EnsureSurface(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (Surface != null && Surface.Width == width && Surface.Height == height)
            {
                return;
            }

            if (Surface != null)
            {
                Surface.Dispose();
            }

            Surface = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        }

        /// <summary>把 Surface 呈现到屏幕指定位置。</summary>
        protected void Present(Point location, byte alpha)
        {
            if (Surface == null)
            {
                return;
            }

            if (!IsHandleCreated)
            {
                IntPtr unused = Handle;
            }

            SetBounds(location.X, location.Y, Surface.Width, Surface.Height);
            UpdateLayered(alpha);
        }

        private void UpdateLayered(byte alpha)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr previousObject = IntPtr.Zero;

            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                hBitmap = Surface.GetHbitmap(Color.FromArgb(0));
                previousObject = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE();
                size.CX = Surface.Width;
                size.CY = Surface.Height;

                NativeMethods.POINT destination = new NativeMethods.POINT();
                destination.X = Left;
                destination.Y = Top;

                NativeMethods.POINT source = new NativeMethods.POINT();

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = alpha;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                NativeMethods.UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref destination,
                    ref size,
                    memDc,
                    ref source,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA);
            }
            catch (Exception ex)
            {
                AppLog.Error("Layered.Present.Failed", ex);
            }
            finally
            {
                if (previousObject != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, previousObject);
                }

                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }

                if (memDc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memDc);
                }

                if (screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Surface != null)
            {
                Surface.Dispose();
                Surface = null;
            }

            base.Dispose(disposing);
        }
    }
}
