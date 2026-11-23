using System;
using System.Runtime.Serialization;

namespace XiaoDing.Core
{
    /// <summary>任务当前所处的状态。</summary>
    public enum TaskState
    {
        /// <summary>等待提醒。</summary>
        Pending = 0,
        /// <summary>已延后，等待延后时间到达。</summary>
        Snoozed = 1,
        /// <summary>已完成。</summary>
        Done = 2,
        /// <summary>错过提醒（应用未运行期间到期）。</summary>
        Missed = 3
    }

    /// <summary>重复方式。</summary>
    public enum RecurrenceKind
    {
        None = 0,
        Daily = 1,
        Weekly = 2,
        Weekdays = 3,
        /// <summary>按用户勾选的星期重复。</summary>
        Custom = 4,
        /// <summary>每月固定日期，数值存在 RecurrenceInterval 中。</summary>
        MonthlyDay = 5,
        /// <summary>每隔 N 天，数值存在 RecurrenceInterval 中。</summary>
        EveryNDays = 6
    }

    /// <summary>提醒形态。</summary>
    public enum ReminderStyle
    {
        /// <summary>右下角卡片。</summary>
        Card = 0,
        /// <summary>主屏全屏遮罩。</summary>
        FullScreen = 1
    }

    /// <summary>
    /// 单条待办任务。所有时间字段统一使用 UTC 存储，展示时转换为本地时间。
    /// </summary>
    [DataContract]
    public sealed class TodoTask
    {
        [DataMember(Order = 1)]  public Guid Id { get; set; }
        [DataMember(Order = 2)]  public string Title { get; set; }
        [DataMember(Order = 3)]  public string Note { get; set; }
        [DataMember(Order = 4)]  public DateTime DueUtc { get; set; }
        [DataMember(Order = 5)]  public int RemindBeforeMinutes { get; set; }
        [DataMember(Order = 6)]  public RecurrenceKind Recurrence { get; set; }
        [DataMember(Order = 7)]  public int WeekdaysMask { get; set; }
        [DataMember(Order = 8)]  public ReminderStyle Style { get; set; }
        [DataMember(Order = 9)]  public bool Enabled { get; set; }
        [DataMember(Order = 10)] public TaskState State { get; set; }
        [DataMember(Order = 11)] public DateTime? SnoozeUntilUtc { get; set; }
        [DataMember(Order = 12)] public DateTime? LastFiredUtc { get; set; }
        [DataMember(Order = 13)] public DateTime CreatedUtc { get; set; }
        [DataMember(Order = 14)] public DateTime? CompletedUtc { get; set; }
        [DataMember(Order = 15)] public string Group { get; set; }
        /// <summary>间隔参数：每月某日的日号，或每 N 天的天数。</summary>
        [DataMember(Order = 16)] public int RecurrenceInterval { get; set; }
        /// <summary>置顶，列表里优先展示。</summary>
        [DataMember(Order = 17)] public bool Pinned { get; set; }

        public TodoTask()
        {
            Id = Guid.NewGuid();
            Title = string.Empty;
            Note = string.Empty;
            Group = string.Empty;
            Enabled = true;
            State = TaskState.Pending;
            Recurrence = RecurrenceKind.None;
            Style = ReminderStyle.Card;
            CreatedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// 反序列化不走构造函数，缺字段时会得到 false/0，导致任务被静默禁用。
        /// 这里先铺一层默认值，再由文件里的字段覆盖。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Id = Guid.NewGuid();
            Title = string.Empty;
            Note = string.Empty;
            Group = string.Empty;
            Enabled = true;
            State = TaskState.Pending;
            Recurrence = RecurrenceKind.None;
            Style = ReminderStyle.Card;
            CreatedUtc = DateTime.UtcNow;
        }

        /// <summary>本次应当触发提醒的时刻：已延后取延后时间，否则取到期时间减去提前量。</summary>
        public DateTime GetFireUtc()
        {
            if (SnoozeUntilUtc.HasValue)
            {
                return SnoozeUntilUtc.Value;
            }

            return DueUtc.AddMinutes(-RemindBeforeMinutes);
        }

        /// <summary>是否仍需要参与调度。</summary>
        public bool IsActive
        {
            get { return Enabled && State != TaskState.Done; }
        }

        public TodoTask Clone()
        {
            return (TodoTask)MemberwiseClone();
        }
    }
}
