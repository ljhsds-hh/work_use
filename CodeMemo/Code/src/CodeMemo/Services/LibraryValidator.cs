using CodeMemo.Models;

namespace CodeMemo.Services;

/// <summary>
/// 导入数据的条目校验（纯函数，可单测）：
/// 不合规条目直接跳过并给出原因，避免脏数据静默进库。
/// 注意只校验「能不能用」，不校验分组名是否在 Catalog 预置列表里 ——
/// 用户自己输入的新子分组是合法数据（见 CommandTreeBuilder）。
/// </summary>
public static class LibraryValidator
{
    public static (List<CommandEntry> Valid, List<string> Problems) Validate(IEnumerable<CommandEntry>? entries)
    {
        var valid = new List<CommandEntry>();
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Title))
            {
                problems.Add("跳过无标题的条目");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Command))
            {
                problems.Add($"跳过无命令内容的条目：{entry.Title}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Category))
            {
                problems.Add($"跳过无大分类的条目：{entry.Title}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.Group))
            {
                problems.Add($"跳过无子分组的条目：{entry.Title}");
                continue;
            }

            // Id 缺失或重复时补一个，保证列表/选中/删除按 Id 定位始终可靠
            if (string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
            {
                if (!string.IsNullOrWhiteSpace(entry.Id))
                {
                    problems.Add($"条目 Id 重复，已重新分配：{entry.Title}");
                }
                entry.Id = Guid.NewGuid().ToString("N");
                ids.Add(entry.Id);
            }

            valid.Add(entry);
        }

        return (valid, problems);
    }
}
