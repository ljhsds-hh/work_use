using System.IO;

namespace QuietRemind.Services;

/// <summary>
/// 单实例互斥与"唤起已有实例"信号（需求 7.3）。
/// 命名空间选择须全系统一致：主实例把选择写入协调文件（存于用户/守护进程视图一致的日志目录），
/// 后续实例优先按同一命名空间探测，避免不同令牌的进程各自落入不同命名空间形成双主。
/// </summary>
public sealed class SingleInstance
{
    public const string GlobalMutexName = @"Global\QuietRemind";
    public const string LocalMutexName = @"Local\QuietRemind";
    public const string ShowMainEventSuffix = "QuietRemind_ShowMain";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showMainEvent;
    private readonly string _nsFilePath;
    private readonly string _namespace;

    public SingleInstance(string nsFilePath)
    {
        _nsFilePath = nsFilePath;
        var preferred = ReadPreferredNamespace();
        var order = preferred == "Local"
            ? new[] { LocalMutexName, GlobalMutexName }
            : new[] { GlobalMutexName, LocalMutexName };

        Mutex? chosen = null;
        var primary = false;
        string ns = LocalMutexName;
        foreach (var name in order)
        {
            try
            {
                // 已存在同名内核对象时为"打开"：与持有方操作同一对象，WaitOne 即可判定互斥
                var mutex = new Mutex(false, name);
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // 前一实例异常退出未释放：互斥体已交予本进程，视为取得
                    acquired = true;
                }
                chosen = mutex;
                primary = acquired;
                ns = name;
                break;
            }
            catch (UnauthorizedAccessException)
            {
                // 该命名空间对当前令牌不可用：尝试下一个
            }
        }

        _mutex = chosen ?? new Mutex(false, LocalMutexName);
        IsPrimary = primary;
        _namespace = ns;
        _showMainEvent = primary ? CreateShowMainEvent(ns) : null;
        if (primary)
        {
            WritePreferredNamespace(ns == GlobalMutexName ? "Global" : "Local");
        }
    }

    /// <summary>本进程是否为唯一主实例（false 表示已有实例在运行）。</summary>
    public bool IsPrimary { get; }

    /// <summary>通知已有实例弹出主界面（由手动启动的重复实例调用）。</summary>
    public static void SignalShowMain(string nsFilePath)
    {
        var preferred = ReadPreferredNamespace(nsFilePath);
        var order = preferred == "Local"
            ? new[] { @"Local\" + ShowMainEventSuffix, @"Global\" + ShowMainEventSuffix }
            : new[] { @"Global\" + ShowMainEventSuffix, @"Local\" + ShowMainEventSuffix };
        foreach (var name in order)
        {
            if (EventWaitHandle.TryOpenExisting(name, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
                return;
            }
        }
    }

    /// <summary>轮询消费"显示主界面"信号（并入 1s 主轮询，无常驻后台线程），收到返回 true。</summary>
    public bool TryConsumeShowMainSignal()
    {
        if (_showMainEvent is null)
        {
            return false;
        }
        try
        {
            return _showMainEvent.WaitOne(0);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadPreferredNamespace(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string? ReadPreferredNamespace() => ReadPreferredNamespace(_nsFilePath);

    private void WritePreferredNamespace(string ns)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_nsFilePath)!);
            File.WriteAllText(_nsFilePath, ns);
        }
        catch (IOException)
        {
            // 协调文件写入失败不影响本实例运行
        }
    }

    private static EventWaitHandle CreateShowMainEvent(string mutexNs)
    {
        // 事件与互斥体使用同一命名空间，保证信号送达同一主实例
        var name = (mutexNs == GlobalMutexName ? @"Global\" : @"Local\") + ShowMainEventSuffix;
        return new EventWaitHandle(false, EventResetMode.AutoReset, name);
    }
}
