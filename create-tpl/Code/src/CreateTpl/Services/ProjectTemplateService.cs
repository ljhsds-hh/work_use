using System.IO;
using System.Text;
using CreateTpl.Models;

namespace CreateTpl.Services;

/// <summary>
/// 标准化工程模板创建服务：目录创建、文件写入、防重复、失败回滚与批量执行。
/// 仅依赖文件系统 API，不依赖任何 UI 类型，可完整单元测试。
/// </summary>
public class ProjectTemplateService : IProjectTemplateService
{
    /// <summary>CLAUDE.md 预置固定文案（需求 3.3）。</summary>
    public const string ClaudeMemoText =
        "此处为工程关键记忆文件，请记录项目核心需求、开发规范、关键逻辑等关键信息，供AI迭代开发、上下文复用使用";

    public async Task<IReadOnlyList<ProjectResult>> CreateBatchAsync(
        string rootDirectory,
        IReadOnlyList<string> projectNames,
        IProgress<ProjectResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProjectResult>(projectNames.Count);

        // 根目录不存在则自动创建（需求 3.1）；创建失败（如权限不足）则整批失败并说明原因
        try
        {
            Directory.CreateDirectory(rootDirectory);
        }
        catch (Exception ex)
        {
            foreach (var name in projectNames)
            {
                var failed = ProjectResult.Failed(name, $"根目录创建失败：{DescribeError(ex)}");
                results.Add(failed);
                progress?.Report(failed);
            }

            return results;
        }

        // 按输入顺序逐个创建；单点失败隔离，不中断后续工程（需求 3.6）
        foreach (var name in projectNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await CreateProjectAsync(rootDirectory, name, cancellationToken);
            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }

    public async Task<ProjectResult> CreateProjectAsync(
        string rootDirectory,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var projectDir = Path.Combine(rootDirectory, projectName);
        try
        {
            // 防重复：目标工程文件夹已存在则跳过，绝不覆盖已有文件（需求 3.4）
            if (Directory.Exists(projectDir))
            {
                return ProjectResult.Skipped(projectName, "当前工程已存在，无需重复创建");
            }

            // 创建固定目录结构（需求 3.2：不增删、不修改层级）
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(Path.Combine(projectDir, "Code"));
            Directory.CreateDirectory(Path.Combine(projectDir, "Docs"));

            // 写入空白文件与预置文本（需求 3.3：README.md / Docs/需求.md 完全空白，CLAUDE.md 固定文案）
            await File.WriteAllTextAsync(Path.Combine(projectDir, "README.md"), string.Empty, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(projectDir, "Docs", "需求.md"), string.Empty, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(projectDir, "CLAUDE.md"), ClaudeMemoText, cancellationToken);

            return ProjectResult.Success(projectName, Path.GetFullPath(projectDir));
        }
        catch (Exception ex)
        {
            // 单点失败隔离：记录中文失败原因，并尽力回滚已创建的残缺目录，避免污染根目录
            var rollbackNote = string.Empty;
            try
            {
                if (Directory.Exists(projectDir))
                {
                    Directory.Delete(projectDir, recursive: true);
                }
            }
            catch (Exception rollbackEx)
            {
                rollbackNote = $"（回滚未成功，可能存在残缺目录：{rollbackEx.Message}）";
            }

            return ProjectResult.Failed(projectName, $"创建失败：{DescribeError(ex)}{rollbackNote}");
        }
    }

    public string BuildTreePreview(string rootDirectory, string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}/{projectName}");
        sb.AppendLine("├── CLAUDE.md    （工程AI关键记忆文件，已预置说明文本）");
        sb.AppendLine("├── README.md    （空白文件）");
        sb.AppendLine("├── Code/        （空文件夹，用于存放项目源码）");
        sb.AppendLine("└── Docs/");
        sb.Append("    └── 需求.md   （空白文档，用于存放项目需求文档）");
        return sb.ToString();
    }

    /// <summary>将底层异常翻译为中文友好提示（需求 3.4：程序不闪退、报错友好）。</summary>
    private static string DescribeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "路径访问权限不足，请检查文件夹权限后重试。",
        IOException ioEx => $"文件系统错误：{ioEx.Message}",
        ArgumentException => "路径格式非法，请检查路径是否包含非法字符。",
        _ => $"意外错误：{ex.Message}"
    };
}
