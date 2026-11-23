using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XiaoDing.Core
{
    /// <summary>全局快捷键。注册失败时不会抛异常，由调用方决定如何提示。</summary>
    public sealed class GlobalHotkey : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0x5A44;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModNoRepeat = 0x4000;

        private bool _registered;

        public bool Register()
        {
            CreateParams parameters = new CreateParams();
            parameters.Caption = "XiaoDing.HotkeyWindow";
            CreateHandle(parameters);

            _registered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, (uint)Keys.N);
            return _registered;
        }

        public bool IsRegistered
        {
            get { return _registered; }
        }

        public event EventHandler Pressed;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                EventHandler handler = Pressed;
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
            if (_registered)
            {
                UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
