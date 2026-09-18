namespace QuietRemind.Services;

/// <summary>单实例互斥与"唤起已有实例"信号（需求 7.3）。</summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\QuietRemind";
    public const string ShowMainEventName = @"Local\QuietRemind_ShowMain";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showMainEvent;
    private bool _disposed;

    public SingleInstance()
    {
        _mutex = new Mutex(false, MutexName);
        bool acquired;
        try
        {
            acquired = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 前一个实例异常退出未释放：互斥体已交予本进程，视为取得
            acquired = true;
        }
        IsPrimary = acquired;
        _showMainEvent = acquired
            ? new EventWaitHandle(false, EventResetMode.AutoReset, ShowMainEventName)
            : null;
    }

    /// <summary>本进程是否为唯一主实例（false 表示已有实例在运行）。</summary>
    public bool IsPrimary { get; }

    /// <summary>通知已有实例弹出主界面（由手动启动的重复实例调用）。</summary>
    public static void SignalShowMain()
    {
        if (EventWaitHandle.TryOpenExisting(ShowMainEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    /// <summary>后台监听"显示主界面"信号，收到后回调（UI 线程封送由调用方保证）。</summary>
    public void StartShowMainWatcher(Action onShowMain)
    {
        if (_showMainEvent is null)
        {
            return;
        }
        var thread = new Thread(() =>
        {
            while (!_disposed && _showMainEvent.WaitOne())
            {
                try
                {
                    onShowMain();
                }
                catch (Exception)
                {
                    // 忽略：监听异常不致命
                }
            }
        })
        {
            IsBackground = true,
            Name = "QuietRemind ShowMain Watcher",
        };
        thread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _showMainEvent?.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
