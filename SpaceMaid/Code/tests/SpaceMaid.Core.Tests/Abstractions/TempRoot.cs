namespace SpaceMaid.Core.Tests.Abstractions;

/// <summary>
/// IO 测试专用临时根目录。
/// 为什么必须统一封装：实施计划 Global Constraints 要求一切 IO 测试的根目录都建在
/// <c>%TEMP%\spacemaid-tests-&lt;guid&gt;</c> 下并在 finally 中删除，
/// 绝对不能触碰 C:\Windows、用户目录等真实系统路径；集中一处可以避免个别用例写错路径。
/// </summary>
internal sealed class TempRoot : IDisposable
{
    /// <summary>创建并返回一个全新的临时根目录（保证唯一，避免并行用例互相干扰）。</summary>
    public TempRoot()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "spacemaid-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>临时根目录的绝对路径。</summary>
    public string Path { get; }

    /// <summary>在根目录下拼一个子路径（不负责创建）。</summary>
    public string Combine(params string[] parts)
    {
        string[] all = new string[parts.Length + 1];
        all[0] = Path;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return System.IO.Path.Combine(all);
    }

    /// <summary>写一个测试文件并返回其绝对路径。</summary>
    public string WriteFile(string relativePath, string content)
    {
        string full = Combine(relativePath);
        string? directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>删除整个临时根目录；失败不影响测试结论（可能被杀软/索引句柄短暂占用）。</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不是被测行为，忽略。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }
}
