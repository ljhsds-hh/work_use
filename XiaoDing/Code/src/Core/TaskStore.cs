using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace XiaoDing.Core
{
    /// <summary>任务文件的根结构，便于以后扩展版本迁移。</summary>
    [DataContract]
    internal sealed class TaskFileModel
    {
        [DataMember(Order = 1)]
        public int Version { get; set; }

        [DataMember(Order = 2)]
        public List<TodoTask> Tasks { get; set; }
    }

    /// <summary>
    /// 任务持久化。写入采用「临时文件 + 替换」保证不产生半截文件。
    /// </summary>
    public sealed class TaskStore
    {
        private const int CurrentVersion = 1;
        private static readonly object Gate = new object();

        public List<TodoTask> Load()
        {
            lock (Gate)
            {
                string path = AppPaths.TaskFile;
                if (!File.Exists(path))
                {
                    return new List<TodoTask>();
                }

                try
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TaskFileModel));
                        TaskFileModel model = serializer.ReadObject(stream) as TaskFileModel;
                        if (model == null || model.Tasks == null)
                        {
                            return new List<TodoTask>();
                        }

                        List<TodoTask> result = new List<TodoTask>(model.Tasks.Count);
                        foreach (TodoTask task in model.Tasks)
                        {
                            if (task != null)
                            {
                                Normalize(task);
                                result.Add(task);
                            }
                        }

                        return result;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Store.Load.Failed", ex);
                    QuarantineBrokenFile(path);
                    return new List<TodoTask>();
                }
            }
        }

        public bool Save(List<TodoTask> tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException("tasks");
            }

            lock (Gate)
            {
                string path = AppPaths.TaskFile;
                string tempPath = path + ".tmp";

                try
                {
                    TaskFileModel model = new TaskFileModel();
                    model.Version = CurrentVersion;
                    model.Tasks = tasks;

                    byte[] payload;
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TaskFileModel));
                        serializer.WriteObject(buffer, model);
                        payload = buffer.ToArray();
                    }

                    File.WriteAllBytes(tempPath, payload);
                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, null);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Store.Save.Failed", ex);
                    TryDelete(tempPath);
                    return false;
                }
            }
        }

        private static void Normalize(TodoTask task)
        {
            if (string.IsNullOrWhiteSpace(task.Title))
            {
                task.Title = "未命名任务";
            }

            if (task.Note == null)
            {
                task.Note = string.Empty;
            }

            if (task.Group == null)
            {
                task.Group = string.Empty;
            }

            if (task.RemindBeforeMinutes < 0)
            {
                task.RemindBeforeMinutes = 0;
            }

            if (task.WeekdaysMask < 0)
            {
                task.WeekdaysMask = 0;
            }

            if (task.Recurrence == RecurrenceKind.MonthlyDay)
            {
                task.RecurrenceInterval = Math.Max(1, Math.Min(31, task.RecurrenceInterval == 0 ? 1 : task.RecurrenceInterval));
            }
            else if (task.Recurrence == RecurrenceKind.EveryNDays)
            {
                task.RecurrenceInterval = Math.Max(1, Math.Min(365, task.RecurrenceInterval == 0 ? 2 : task.RecurrenceInterval));
            }
            else if (task.RecurrenceInterval < 0)
            {
                task.RecurrenceInterval = 0;
            }

            // 反序列化缺字段时 DueUtc 是 0001-01-01，会被当成千年逾期立刻触发，矫正为 1 小时后。
            if (task.DueUtc == default(DateTime))
            {
                task.DueUtc = DateTime.UtcNow.AddHours(1);
            }

            if (task.DueUtc.Kind != DateTimeKind.Utc)
            {
                task.DueUtc = DateTime.SpecifyKind(task.DueUtc, DateTimeKind.Utc);
            }
        }

        private static void QuarantineBrokenFile(string path)
        {
            try
            {
                string backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Move(path, backup);
                AppLog.Warn("Store.Load.Quarantined", backup);
            }
            catch (Exception ex)
            {
                AppLog.Error("Store.Load.QuarantineFailed", ex);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 忽略清理失败。
            }
        }
    }
}
