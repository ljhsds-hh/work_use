using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace XiaoDing.Core
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Order = 1)] public bool AutoStart { get; set; }
        [DataMember(Order = 2)] public bool Paused { get; set; }
        /// <summary>全屏遮罩的暗度，0 到 255。</summary>
        [DataMember(Order = 3)] public int OverlayDim { get; set; }
        /// <summary>深色模式开关。</summary>
        [DataMember(Order = 4)] public bool DarkMode { get; set; }

        public AppSettings()
        {
            OverlayDim = 150;
        }
    }

    /// <summary>应用设置持久化，损坏时回退到默认值。</summary>
    public sealed class SettingsStore
    {
        private static readonly object Gate = new object();

        public AppSettings Load()
        {
            lock (Gate)
            {
                try
                {
                    string path = AppPaths.SettingsFile;
                    if (!File.Exists(path))
                    {
                        return new AppSettings();
                    }

                    using (FileStream stream = File.OpenRead(path))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                        AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                        if (settings == null)
                        {
                            return new AppSettings();
                        }

                        settings.OverlayDim = Math.Max(0, Math.Min(255, settings.OverlayDim));
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Settings.Load.Failed", ex);
                    return new AppSettings();
                }
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            lock (Gate)
            {
                string path = AppPaths.SettingsFile;
                string tempPath = path + ".tmp";
                try
                {
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                        serializer.WriteObject(buffer, settings);
                        File.WriteAllBytes(tempPath, buffer.ToArray());
                    }

                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, null);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Settings.Save.Failed", ex);
                }
            }
        }
    }
}
