using System;
using System.IO;

namespace XiaoDing.Core
{
    /// <summary>
    /// 应用运行期使用的本地路径。全部位于当前用户的漫游配置目录下，不触碰系统目录。
    /// </summary>
    public static class AppPaths
    {
        public static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XiaoDing");

        public static string TaskFile { get { return Path.Combine(Root, "tasks.json"); } }

        public static string LogDirectory { get { return Path.Combine(Root, "logs"); } }

        public static string OverlayImagePath { get { return Path.Combine(Root, "overlay.png"); } }

        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }

        public static void EnsureCreated()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogDirectory);
        }
    }
}
