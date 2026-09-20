using System.Security.Principal;
using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="IEnvironmentProbe"/> 的 Windows 实现。
/// </summary>
public sealed class WindowsEnvironmentProbe : IEnvironmentProbe
{
    /// <inheritdoc />
    /// <remarks>
    /// 为什么用 <see cref="WindowsIdentity.GetCurrent()"/> + <c>IsInRole(Administrator)</c>：
    /// 这是判断"令牌是否真的带管理员组"的标准做法；相比读注册表或试探写入更轻、无副作用。
    /// 任何失败都按"未提权"处理——这是安全方向上的保守默认（设计文档 §3.1 I-4）。
    /// </remarks>
    public bool IsElevated
    {
        get
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 取系统盘符（形如 <c>C:</c>）：优先用 <c>%SystemDrive%</c>（需求与清单模板都用它），
    /// 失败时回退到系统目录所在的卷根，保证永不返回空串。
    /// </remarks>
    public string SystemDrive
    {
        get
        {
            try
            {
                string? fromVariable = Environment.GetEnvironmentVariable("SystemDrive");
                if (!string.IsNullOrWhiteSpace(fromVariable))
                {
                    return NormalizeDrive(fromVariable);
                }
            }
            catch (Exception)
            {
                // 落到下面的回退分支。
            }

            try
            {
                return NormalizeDrive(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
            }
            catch (Exception)
            {
                return "C:";
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 为什么不直接 <c>Environment.ExpandEnvironmentVariables</c>：它会把**未知变量**替换成空串，
    /// 而设计文档 §3.2 第 3 步要求"未知变量原样保留"以便 PathNormalizer 判为未展开变量并拒绝。
    /// 因此这里只替换已登记（当前进程能取到值）的变量，并支持 <c>~</c> 展开为用户目录。
    /// </remarks>
    public string ExpandVariables(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw ?? string.Empty;
        }

        string expanded = ExpandKnownVariables(raw);
        return ExpandTilde(expanded);
    }

    private static string ExpandKnownVariables(string raw)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length);
        int index = 0;

        while (index < raw.Length)
        {
            char current = raw[index];
            if (current != '%')
            {
                builder.Append(current);
                index++;
                continue;
            }

            int closing = raw.IndexOf('%', index + 1);
            // IndexOf 找不到返回 -1，此时 closing <= index 必然成立，两个分支合并处理即可。
            if (closing <= index + 1)
            {
                // 单独的 '%' 或空变量名 '%...%' 不是变量语法：原样保留。
                builder.Append(current);
                index++;
                continue;
            }

            string name = raw.Substring(index + 1, closing - index - 1);
            string? value = TryGetVariable(name);
            if (value is null)
            {
                // 未知变量：连百分号一起原样保留。
                builder.Append(raw, index, closing - index + 1);
            }
            else
            {
                builder.Append(value);
            }

            index = closing + 1;
        }

        return builder.ToString();
    }

    private static string? TryGetVariable(string name)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ExpandTilde(string raw)
    {
        if (raw.Length == 0 || raw[0] != '~')
        {
            return raw;
        }

        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile))
            {
                return raw;
            }

            return raw.Length == 1 ? profile : profile + raw.Substring(1);
        }
        catch (Exception)
        {
            return raw;
        }
    }

    private static string NormalizeDrive(string drive)
    {
        string trimmed = drive.Trim().TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return "C:";
        }

        // 只保留形如 "C:" 的盘符；其他形态（UNC、空）不做假定，直接返回裁剪结果。
        return trimmed.Length >= 2 && trimmed[1] == ':' ? trimmed.Substring(0, 2) : trimmed;
    }
}
