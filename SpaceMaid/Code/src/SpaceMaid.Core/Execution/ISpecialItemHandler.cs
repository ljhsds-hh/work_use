using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.Core.Execution;

/// <summary>
/// 专项动作处理器（休眠、DISM 等无法用"搬文件"表达的动作）。
/// 由 Task 11 实现并注入执行器；没有注入时这些项会被如实标为"未执行"。
/// </summary>
public interface ISpecialItemHandler
{
    /// <summary>能否处理该动作类型。</summary>
    bool CanHandle(CleanActionKind kind);

    /// <summary>执行并在失败时返回可展示的原因（不抛异常）。</summary>
    ItemExecutionResult Handle(CleanPlanItem item, ExecutionOptions options, CancellationToken cancellationToken);
}
