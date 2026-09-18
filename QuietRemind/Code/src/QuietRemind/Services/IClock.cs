namespace QuietRemind.Services;

/// <summary>时钟抽象：核心逻辑依赖此接口获取当前时间，便于测试注入可控时钟。</summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime Now => DateTime.Now;
}
