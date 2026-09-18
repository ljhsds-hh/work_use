using QuietRemind.Services;

namespace QuietRemind.Tests;

/// <summary>可拨动的可控时钟，供核心逻辑单元测试注入。</summary>
public sealed class FakeClock : IClock
{
    public DateTime Now { get; set; }

    public FakeClock(DateTime now) => Now = now;

    public void Advance(TimeSpan span) => Now = Now.Add(span);

    public void Set(DateTime now) => Now = now;
}
