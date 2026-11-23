using System;
using System.Threading;
using System.Windows.Forms;

namespace XiaoDing.Core
{
    /// <summary>
    /// 单实例控制。第二个实例启动时广播消息唤起第一实例的主窗口并自行退出。
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\XiaoDing.SingleInstance.v1";
        private const string ActivateMessageName = "XiaoDing.ActivateMainWindow";

        private static readonly int ActivateMessage = (int)NativeMethods.RegisterWindowMessage(ActivateMessageName);

        private Mutex _mutex;
        private bool _ownsMutex;

        public bool IsFirstInstance { get; private set; }

        public SingleInstance()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            IsFirstInstance = createdNew;

            if (createdNew)
            {
                _ownsMutex = true;
            }
        }

        /// <summary>通知已在运行的实例显示主窗口。</summary>
        public static void SignalExisting()
        {
            NativeMethods.PostMessage(new IntPtr(NativeMethods.HWND_BROADCAST), (uint)ActivateMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public bool IsActivateMessage(Message message)
        {
            return message.Msg == ActivateMessage;
        }

        public void Dispose()
        {
            if (_mutex == null)
            {
                return;
            }

            if (_ownsMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 未持有互斥量时忽略。
                }

                _ownsMutex = false;
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
