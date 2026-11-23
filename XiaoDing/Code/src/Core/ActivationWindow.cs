using System;
using System.Windows.Forms;

namespace XiaoDing.Core
{
    /// <summary>
    /// 接收其他实例发来的广播消息。必须是顶层窗口才能收到 HWND_BROADCAST，
    /// 这里创建的是不可见顶层窗口。
    /// </summary>
    internal sealed class ActivationWindow : NativeWindow, IDisposable
    {
        private readonly SingleInstance _instance;

        public ActivationWindow(SingleInstance instance)
        {
            _instance = instance;

            CreateParams parameters = new CreateParams();
            parameters.Caption = "XiaoDing.ActivationWindow";
            CreateHandle(parameters);
        }

        public event EventHandler ActivateRequested;

        protected override void WndProc(ref Message m)
        {
            if (_instance.IsActivateMessage(m))
            {
                EventHandler handler = ActivateRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }

                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
