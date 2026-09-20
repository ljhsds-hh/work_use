using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>写盘节流：短时间多次改动只写一次，关键点能立刻落盘，也能丢弃。</summary>
public class DeferredSaverTests
{
    private readonly FakeSaveScheduler _scheduler = new();
    private int _saveCount;

    private DeferredSaver Create(TimeSpan? delay = null)
        => new(() => _saveCount++, _scheduler, delay);

    [Fact]
    public void 连续登记改动_到点只写一次()
    {
        var saver = Create();

        saver.RequestSave();
        saver.RequestSave();
        saver.RequestSave();

        Assert.True(saver.HasPendingChanges);
        Assert.Equal(3, _scheduler.Scheduled.Count);   // 每次都重新排定（覆盖上一次）
        Assert.Equal(0, _saveCount);                   // 到点前不写

        _scheduler.Fire();

        Assert.Equal(1, _saveCount);
        Assert.False(saver.HasPendingChanges);
    }

    [Fact]
    public void 到点回调触发两次也只写一次()
    {
        var saver = Create();
        saver.RequestSave();

        _scheduler.Fire();
        _scheduler.Fire();

        Assert.Equal(1, _saveCount);
    }

    [Fact]
    public void SaveNow_立刻落盘并取消排定()
    {
        var saver = Create();
        saver.RequestSave();

        saver.SaveNow();

        Assert.Equal(1, _saveCount);
        Assert.False(saver.HasPendingChanges);
        Assert.True(_scheduler.CancelCount > 0);

        _scheduler.Fire();          // 排定已取消，再来一次也不该重复写
        Assert.Equal(1, _saveCount);
    }

    [Fact]
    public void 没有待写改动时_SaveNow_不写盘()
    {
        var saver = Create();

        saver.SaveNow();

        Assert.Equal(0, _saveCount);
        Assert.False(saver.HasPendingChanges);
    }

    [Fact]
    public void CancelPending_丢弃待写改动()
    {
        var saver = Create();
        saver.RequestSave();

        saver.CancelPending();
        _scheduler.Fire();

        Assert.Equal(0, _saveCount);
        Assert.False(saver.HasPendingChanges);
    }

    [Fact]
    public void 默认延迟是1点5秒_也可以自定义()
    {
        var saver = Create();
        saver.RequestSave();
        Assert.Equal(DeferredSaver.DefaultDelay, _scheduler.Scheduled[^1]);

        var custom = Create(TimeSpan.FromSeconds(5));
        custom.RequestSave();
        Assert.Equal(TimeSpan.FromSeconds(5), _scheduler.Scheduled[^1]);
    }

    [Fact]
    public void 写盘后还能继续登记()
    {
        var saver = Create();
        saver.RequestSave();
        _scheduler.Fire();

        saver.RequestSave();
        Assert.True(saver.HasPendingChanges);
        _scheduler.Fire();

        Assert.Equal(2, _saveCount);
    }
}
