using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace XiaoDing.Core
{
    /// <summary>
    /// 轻量本地日志。按天分文件，单文件上限 1 MB，保留最近 7 天。
    /// 只记录事件名与异常摘要，不写入任务标题等用户内容。
    /// </summary>
    public static class AppLog
    {
        private const long MaxFileBytes = 1024 * 1024;
        private const int KeepDays = 7;
        private const string FilePrefix = "xiaoding-";

        private static readonly object Gate = new object();

        public static void Info(string eventName)
        {
            Write("INFO", eventName, null);
        }

        public static void Warn(string eventName, string detail)
        {
            Write("WARN", eventName, detail);
        }

        public static void Error(string eventName, Exception exception)
        {
            string detail = null;
            if (exception != null)
            {
                detail = exception.GetType().Name + ": " + SafeMessage(exception.Message);
            }

            Write("ERROR", eventName, detail);
        }

        /// <summary>清理过期日志，启动时调用一次。</summary>
        public static void Purge()
        {
            try
            {
                if (!Directory.Exists(AppPaths.LogDirectory))
                {
                    return;
                }

                DateTime threshold = DateTime.Now.Date.AddDays(-KeepDays);
                foreach (string file in Directory.GetFiles(AppPaths.LogDirectory, FilePrefix + "*.log"))
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Write("WARN", "Log.Purge.Failed", ex.GetType().Name);
            }
        }

        private static void Write(string level, string eventName, string detail)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDirectory);

                    string path = CurrentFile();
                    if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                    {
                        path = Path.Combine(
                            AppPaths.LogDirectory,
                            FilePrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                    }

                    StringBuilder line = new StringBuilder(160);
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    line.Append(" [").Append(level).Append("] ");
                    line.Append(eventName);
                    if (!string.IsNullOrEmpty(detail))
                    {
                        line.Append(" | ").Append(detail);
                    }

                    File.AppendAllText(path, line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志自身失败不得影响主流程。
            }
        }

        private static string CurrentFile()
        {
            return Path.Combine(
                AppPaths.LogDirectory,
                FilePrefix + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        }

        private static string SafeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            string trimmed = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (trimmed.Length > 200)
            {
                trimmed = trimmed.Substring(0, 200);
            }

            return trimmed;
        }
    }
}
