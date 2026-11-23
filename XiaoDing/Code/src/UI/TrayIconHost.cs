using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>托盘图标与右键菜单。</summary>
    internal sealed class TrayIconHost : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _pauseItem;
        private Icon _icon;

        public TrayIconHost()
        {
            _icon = CreateAppIcon();

            _menu = new ContextMenuStrip();
            _menu.Font = Theme.FontBody;

            ToolStripMenuItem openItem = new ToolStripMenuItem("打开小叮");
            openItem.Click += delegate { Raise(OpenRequested); };
            _menu.Items.Add(openItem);

            ToolStripMenuItem newItem = new ToolStripMenuItem("新建任务");
            newItem.Click += delegate { Raise(NewTaskRequested); };
            _menu.Items.Add(newItem);

            _menu.Items.Add(new ToolStripSeparator());

            _pauseItem = new ToolStripMenuItem("暂停全部提醒");
            _pauseItem.CheckOnClick = true;
            _pauseItem.Click += delegate
            {
                EventHandler handler = PauseChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            _menu.Items.Add(_pauseItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { Raise(ExitRequested); };
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _icon;
            _notifyIcon.Text = "小叮 · 待办提醒";
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { Raise(OpenRequested); };
        }

        public event EventHandler OpenRequested;

        public event EventHandler NewTaskRequested;

        public event EventHandler ExitRequested;

        public event EventHandler PauseChanged;

        public bool Paused
        {
            get { return _pauseItem.Checked; }
            set { _pauseItem.Checked = value; }
        }

        /// <summary>程序图标：优先读取内嵌的多尺寸 app.ico，失败时退回品牌色圆形 + 白色「叮」字。</summary>
        public static Icon CreateAppIcon()
        {
            try
            {
                using (System.IO.Stream stream = typeof(TrayIconHost).Assembly
                    .GetManifestResourceStream("XiaoDing.Assets.app.ico"))
                {
                    if (stream != null)
                    {
                        return new Icon(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Tray.Icon.ResourceFailed", ex);
            }

            using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

                using (SolidBrush background = new SolidBrush(Theme.Brand))
                {
                    graphics.FillEllipse(background, 1, 1, 30, 30);
                }

                using (Font font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat())
                using (SolidBrush foreground = new SolidBrush(Color.White))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("叮", font, foreground, new RectangleF(0, 0, 32, 32), format);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_menu != null)
            {
                _menu.Dispose();
            }

            if (_icon != null)
            {
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
