namespace CreateTpl.Helpers;

/// <summary>
/// Windows 保留设备名清单：这些名称不允许作为文件夹名（含"保留名.任意扩展名"形式）。
/// </summary>
public static class ReservedNames
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>判断名称是否为 Windows 保留设备名。</summary>
    public static bool IsReserved(string name)
    {
        // 取主名部分（首个点之前），"CON.txt" 同样属于保留名
        var stem = name.Split('.')[0];
        return Names.Contains(stem);
    }
}
