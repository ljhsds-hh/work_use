using CreateTpl.Models;

namespace CreateTpl.Services;

/// <summary>
/// 标准化工程模板创建服务接口。
/// </summary>
public interface IProjectTemplateService
{
    /// <summary>
    /// 批量创建：按输入顺序逐个生成；单点失败隔离，任一工程失败不影响后续（需求 3.6）。
    /// 根目录不存在时自动创建。
    /// </summary>
    Task<IReadOnlyList<ProjectResult>> CreateBatchAsync(
        string rootDirectory,
        IReadOnlyList<string> projectNames,
        IProgress<ProjectResult>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建单个标准化工程（需求 3.2 / 3.3 / 3.4）。</summary>
    Task<ProjectResult> CreateProjectAsync(
        string rootDirectory,
        string projectName,
        CancellationToken cancellationToken = default);

    /// <summary>生成单个工程的目录树预览文本（需求 3.5）。</summary>
    string BuildTreePreview(string rootDirectory, string projectName);
}
