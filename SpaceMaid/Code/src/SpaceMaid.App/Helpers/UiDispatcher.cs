using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceMaid.App.Helpers;

/// <summary>
/// UI 线程调度器：ViewModel 的所有回调都必须回到 UI 线程再改属性，
/// 否则后台线程改 <c>ObservableCollection</c> 会让 WPF 抛 <c>NotSupportedException</c>。
/// 测试环境没有 SynchronizationContext，此时保持"同步执行"，测试就不必自己造 Dispatcher。
/// </summary>
public sealed class UiDispatcher(SynchronizationContext? context)
{
    private readonly SynchronizationContext? _context = context;

    public static UiDispatcher Capture() => new(SynchronizationContext.Current);

    public bool HasContext => _context is not null;

    public void Post(Action action)
    {
        if (_context is null)
        {
            action();
            return;
        }

        _context.Post(_ => action(), null);
    }

    /// <summary>等待一个在 UI 线程上完成的动作（内部用同步原语占位，避免死等）。</summary>
    public async Task RunAsync(Action action)
    {
        if (_context is null)
        {
            action();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(
            _ =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            },
            null);

        await completion.Task.ConfigureAwait(true);
    }
}
