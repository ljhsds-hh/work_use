using System;
using System.Threading;
using System.Windows.Forms;
using XiaoDing.Core;
using XiaoDing.UI;

namespace XiaoDing
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppPaths.EnsureCreated();
            AppLog.Purge();

            bool silentStart = HasArgument(args, "--startup");

            using (SingleInstance instance = new SingleInstance())
            {
                if (!instance.IsFirstInstance)
                {
                    SingleInstance.SignalExisting();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainException;

                Theme.Initialize();

                try
                {
                    Application.Run(new XiaoDingAppContext(instance, silentStart));
                }
                catch (Exception ex)
                {
                    AppLog.Error("App.Run.Failed", ex);
                    MessageBox.Show(
                        "小叮启动失败：" + ex.Message,
                        "小叮",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            AppLog.Error("App.ThreadException", e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.Error("App.DomainException", e.ExceptionObject as Exception);
        }
    }
}
