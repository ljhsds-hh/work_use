namespace CodeMemo.Services;

/// <summary>
/// 延迟执行回调的调度器。真正跑的时候用 Dispatcher 版实现（<c>DispatcherSaveScheduler</c>），
/// 测试里换成假的，能手动决定「什么时候到点」。
/// </summary>
public interface ISaveScheduler
{
    /// <summary>安排一次延迟回调；同一调度器重复安排会覆盖上一次（这就是去抖）。</summary>
    void Schedule(TimeSpan delay, Action callback);

    /// <summary>取消尚未触发的回调。</summary>
    void Cancel();
}

/// <summary>
/// 写盘节流器：把短时间内的多次「需要落盘」合并成一次整库写。
///
/// 复制命令会改使用次数 / 最近复制时间，按次全库序列化没必要（89 条还好，条数涨起来就是白写盘）。
/// 所以复制只登记改动，静默一小会儿再写；退出、导入这类关键时刻调 <see cref="SaveNow"/> 立刻落盘。
/// </summary>
public sealed class DeferredSaver
{
    /// <summary>默认静默时长：这段时间内没有新的改动就写盘。</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1.5);

    private readonly Action _save;
    private readonly ISaveScheduler _scheduler;
    private readonly TimeSpan _delay;

    private bool _dirty;

    public DeferredSaver(Action save, ISaveScheduler scheduler, TimeSpan? delay = null)
    {
        _save = save;
        _scheduler = scheduler;
        _delay = delay ?? DefaultDelay;
    }

    /// <summary>是否有改动还没落盘。</summary>
    public bool HasPendingChanges => _dirty;

    /// <summary>登记一次改动：延迟到静默期结束再写；期间重复调用只写一次。</summary>
    public void RequestSave()
    {
        _dirty = true;
        _scheduler.Schedule(_delay, Flush);
    }

    /// <summary>立刻落盘（退出 / 导入等关键点）。没有待写改动时什么也不做。</summary>
    public void SaveNow() => Persist(cancelPending: true);

    /// <summary>丢弃待写改动（不写盘）。</summary>
    public void CancelPending()
    {
        _scheduler.Cancel();
        _dirty = false;
    }

    private void Flush() => Persist(cancelPending: false);

    private void Persist(bool cancelPending)
    {
        if (cancelPending)
        {
            _scheduler.Cancel();
        }

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _save();
    }
}
