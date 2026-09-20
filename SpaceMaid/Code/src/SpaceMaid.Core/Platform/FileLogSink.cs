using System.IO;
using System.Text;
using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// 追加写文本日志的日志汇（按天一个文件：<c>spacemaid-yyyyMMdd.log</c>）。
/// <para>
/// 为什么这样实现：
/// ① **静默降级**——日志目录可能不可写（默认目录在 D 盘、U 盘被拔、权限不足），
///    此时任何一次写入失败都会永久关闭本实例，后续调用变成 no-op，绝不抛异常打断清理流程
///    （设计文档 §1.1-3、§10"不可写则回退且不中断"）；
/// ② **按天分文件**——满足"惰性滚动 + 启动时清理 30 天前日志"的语义，不需要常驻线程（设计文档 §1.3）；
/// ③ 每次写入都重新打开并追加，牺牲少量性能换取"进程被杀也不丢行、不占句柄"。
/// </para>
/// </summary>
public sealed class FileLogSink : ILogSink
{
    private readonly IClock _clock;
    private readonly object _gate = new object();
    private readonly string _directory;

    /// <summary>一旦写入失败就永久关闭，避免每次调用都重复撞同一个错误目录。</summary>
    private bool _disabled;

    /// <summary>构造日志汇。</summary>
    /// <param name="logDirectory">日志目录；不存在时会尝试创建，不可写则退化为 no-op。</param>
    /// <param name="clock">用于生成时间戳与"按天"文件名（可注入假时钟以确定性测试）。</param>
    public FileLogSink(string logDirectory, IClock clock)
    {
        _directory = logDirectory ?? string.Empty;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>当前是否已退化为 no-op（供上层在设置页提示"日志已关闭"）。</summary>
    public bool IsDisabled
    {
        get
        {
            lock (_gate)
            {
                return _disabled;
            }
        }
    }

    /// <inheritdoc />
    public void Info(string message) => Write("INFO", message, null);

    /// <inheritdoc />
    public void Warn(string message) => Write("WARN", message, null);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        lock (_gate)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                if (!Directory.Exists(_directory))
                {
                    Directory.CreateDirectory(_directory);
                }

                string line = ComposeLine(level, message, exception);
                string path = Path.Combine(_directory, "spacemaid-" + _clock.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception)
            {
                // 记不了日志不是业务失败：静默关闭自身，绝不向上抛（设计文档 §10）。
                // 这里刻意吞掉全部异常类型（含 IOException/UnauthorizedAccessException/ArgumentException）。
                _disabled = true;
            }
        }
    }

    private string ComposeLine(string level, string message, Exception? exception)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(_clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append(" [").Append(level).Append("] ");
        builder.Append(message ?? string.Empty);

        if (exception is not null)
        {
            // 只记类型与消息，不记堆栈：日志要给人看，且避免敏感路径噪声。
            builder.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        builder.Append(Environment.NewLine);
        return builder.ToString();
    }
}
