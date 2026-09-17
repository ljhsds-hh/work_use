namespace CreateTpl.Models;

/// <summary>
/// 单个工程的创建结果：批量执行中每个工程独立产出一条结果，
/// 供界面汇总展示与未完成清单提醒（需求 3.6）。
/// </summary>
public class ProjectResult
{
    private ProjectResult()
    {
    }

    /// <summary>工程名称。</summary>
    public string ProjectName { get; private init; } = string.Empty;

    /// <summary>创建结果状态。</summary>
    public CreateStatus Status { get; private init; }

    /// <summary>成功时为完整生成路径；跳过/失败时为中文原因说明。</summary>
    public string Message { get; private init; } = string.Empty;

    /// <summary>是否创建成功。</summary>
    public bool IsSuccess => Status == CreateStatus.Success;

    /// <summary>状态中文文案，供界面展示。</summary>
    public string StatusText => Status switch
    {
        CreateStatus.Success => "成功",
        CreateStatus.Skipped => "已存在",
        _ => "失败"
    };

    public static ProjectResult Success(string name, string path) =>
        new() { ProjectName = name, Status = CreateStatus.Success, Message = path };

    public static ProjectResult Skipped(string name, string reason) =>
        new() { ProjectName = name, Status = CreateStatus.Skipped, Message = reason };

    public static ProjectResult Failed(string name, string reason) =>
        new() { ProjectName = name, Status = CreateStatus.Failed, Message = reason };
}
