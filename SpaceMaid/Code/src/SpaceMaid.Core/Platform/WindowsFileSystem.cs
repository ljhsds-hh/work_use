using System.IO;
using System.Security.Cryptography;
using SpaceMaid.Core.Abstractions;

namespace SpaceMaid.Core.Platform;

/// <summary>
/// <see cref="IFileSystem"/> 的 Windows 实现（内核唯一真实碰磁盘的地方）。
/// <para>
/// 设计取舍：
/// ① **只读类成员永不抛异常**（存在性/枚举/时间/哈希），返回空列表或默认值，
///    让扫描引擎能把条目标成"不可用"而不是整轮扫描崩掉（设计文档 §5.1-6）；
/// ② **写类成员只在 <c>Try*</c> 形态下出现**，失败一律转成中文原因字符串返回，
///    因为"某个文件被占用导致没清掉"是业务常态，不是程序缺陷（设计文档 §5.3）；
/// ③ 删除只有 <see cref="TryDeleteFile"/> 一个出口，且只允许 QuarantineStore 调用（I-1/D-3）。
/// </para>
/// </summary>
public sealed class WindowsFileSystem : IFileSystem
{
    /// <summary>快速哈希的头部/尾部采样长度：4MB。</summary>
    private const int SampleBytes = 4 * 1024 * 1024;

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>已存在时视为成功；创建失败（权限/路径非法）不抛异常——后续写入会自然失败并记原因。</remarks>
    public void CreateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            // 静默：调用方会在随后的写入动作里拿到真实失败原因。
        }
    }

    /// <inheritdoc />
    public long GetFileSize(string path)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset GetLastWriteTime(string path)
    {
        try
        {
            return ToOffset(File.GetLastWriteTimeUtc(path));
        }
        catch (Exception)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset GetCreationTime(string path)
    {
        try
        {
            return ToOffset(File.GetCreationTimeUtc(path));
        }
        catch (Exception)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 用 <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> 即时枚举，
    /// 不用 <c>GetFiles</c>：后者会一次性把整棵树的路径数组放进内存，大目录（WinSxS 级）会爆内存。
    /// 枚举过程中若有子目录中途不可访问，跳过该子目录继续（不整体失败）。
    /// </remarks>
    public IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        string effectivePattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
        SearchOption option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        List<string> results = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, effectivePattern, option))
            {
                results.Add(file);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            // 部分子目录不可读：返回已枚举到的部分，扫描引擎按"部分可用"处理。
        }
        catch (IOException)
        {
            // 同上（网络盘断开、路径过长等）。
        }
        catch (Exception)
        {
            // 其他异常同样不向上抛：枚举失败等价于"没有可处理的文件"。
        }

        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateDirectories(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Array.Empty<string>();
        }

        List<string> results = new List<string>();
        try
        {
            foreach (string sub in Directory.EnumerateDirectories(directory))
            {
                results.Add(sub);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception)
        {
        }

        return results;
    }

    /// <inheritdoc />
    /// <remarks>共享模式给 <c>Read</c>，允许其它进程继续写（只需读快照即可计算哈希）。</remarks>
    public Stream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 同卷移动会走 NTFS 原子重命名，不产生额外空间占用（设计文档 §6.3）。
    /// 目标目录不存在时自动创建，避免隔离区批次目录尚未建好就移动。
    /// </remarks>
    public bool TryMove(string source, string destination, out string error)
    {
        error = string.Empty;
        try
        {
            EnsureParentDirectory(destination);
            // overwrite=true：隔离区 payload 文件名带序号保证唯一，出现同名只可能是上次半残留档，覆盖即可。
            File.Move(source, destination, overwrite: true);
            return true;
        }
        catch (FileNotFoundException)
        {
            error = "源文件不存在：" + source;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            error = "源或目标的目录不存在：" + source + " → " + destination;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = "拒绝访问（可能被占用或权限不足）：" + exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            error = "移动失败：" + exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            error = "移动出现异常：" + exception.GetType().Name + ":" + exception.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryCopy(string source, string destination, out string error)
    {
        error = string.Empty;
        try
        {
            EnsureParentDirectory(destination);
            File.Copy(source, destination, overwrite: true);
            return true;
        }
        catch (FileNotFoundException)
        {
            error = "源文件不存在：" + source;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            error = "源或目标的目录不存在：" + source + " → " + destination;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = "拒绝访问（可能被占用或权限不足）：" + exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            error = "复制失败：" + exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            error = "复制出现异常：" + exception.GetType().Name + ":" + exception.Message;
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// **安全提示**：这是全项目唯一的删除出口。调用点只有 QuarantineStore 的
    /// 惰性释放与清空隔离区，且必须发生在"文件已确认进入隔离区"之后（I-1/D-3）。
    /// </remarks>
    public bool TryDeleteFile(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                // 已经不存在等价于"删除成功"（幂等），避免恢复流程里重复删报错。
                return true;
            }

            File.Delete(path);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = "拒绝访问（只读或被占用）：" + exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            error = "删除失败：" + exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            error = "删除出现异常：" + exception.GetType().Name + ":" + exception.Message;
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception)
        {
            // 路径不存在或不可访问：无法证明它是链接，按"不是"处理；
            // 真正的安全判定在 SafetyGate 的存在性/白名单环节完成。
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 逐级向上检查父目录。为什么必须这样做：把 <c>C:\Temp\link\a.txt</c> 里的 <c>link</c>
    /// 当作普通目录处理，删除会穿透到链接指向的真实位置（设计文档 §3.2 第 6 步）。
    /// </remarks>
    public bool HasReparsePointAncestor(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string? cursor = Path.GetDirectoryName(Path.GetFullPath(path));
            while (!string.IsNullOrEmpty(cursor) && cursor != Path.GetPathRoot(cursor))
            {
                if (IsReparsePoint(cursor))
                {
                    return true;
                }

                cursor = Path.GetDirectoryName(cursor);
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 判定方式：尝试以 <see cref="FileShare.None"/> 独占打开。能打开说明没有别的进程占用；
    /// 抛 <see cref="IOException"/>（ERROR_SHARING_VIOLATION）说明被占用。
    /// 用 <c>FileOptions.None</c> 不做缓存/顺序提示，纯粹探测句柄。
    /// </remarks>
    public bool IsFileLocked(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !FileExists(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足也算"不能碰"，落地阶段同样应跳过。
            return true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// full=true：全文件 SHA-256（最终确认重复）。
    /// full=false：只读首尾各 4MB（文件更小则全量），并把**文件长度**一起混入哈希，
    /// 防止"首尾相同但中间不同"的文件被误判为重复（设计文档 §5.2）。
    /// </remarks>
    public string ComputeHash(string path, bool full)
    {
        try
        {
            // 声明为 Stream 而非 FileStream：OpenRead 的契约是抽象接口，实现可替换。
            using Stream stream = OpenRead(path);
            using SHA256 sha = SHA256.Create();

            if (full || stream.Length <= (long)SampleBytes * 2)
            {
                byte[] fullHash = sha.ComputeHash(stream);
                return Convert.ToHexString(fullHash);
            }

            byte[] head = ReadExact(stream, SampleBytes);
            stream.Seek(-SampleBytes, SeekOrigin.End);
            byte[] tail = ReadExact(stream, SampleBytes);

            byte[] lengthBytes = BitConverter.GetBytes(stream.Length);
            byte[] sample = new byte[lengthBytes.Length + head.Length + tail.Length];
            Buffer.BlockCopy(lengthBytes, 0, sample, 0, lengthBytes.Length);
            Buffer.BlockCopy(head, 0, sample, lengthBytes.Length, head.Length);
            Buffer.BlockCopy(tail, 0, sample, lengthBytes.Length + head.Length, tail.Length);

            return Convert.ToHexString(sha.ComputeHash(sample));
        }
        catch (Exception)
        {
            // 读不了就返回空串：调用方（重复文件分组）会跳过空哈希，不会误判为重复。
            return string.Empty;
        }
    }

    private void EnsureParentDirectory(string destination)
    {
        string? parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    /// <summary>
    /// 读满 <paramref name="count"/> 字节（FileStream 一次 Read 可能少于请求量）。
    /// </summary>
    private static byte[] ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int chunk = stream.Read(buffer, read, count - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        if (read == count)
        {
            return buffer;
        }

        byte[] shortened = new byte[read];
        Buffer.BlockCopy(buffer, 0, shortened, 0, read);
        return shortened;
    }

    /// <summary>
    /// 把 UTC <see cref="DateTime"/> 转成带本机时区偏移的 <see cref="DateTimeOffset"/>。
    /// 单独封装的原因：<see cref="DateTime.MinValue"/>（文件不存在时的返回值）在转
    /// <see cref="DateTimeOffset"/> 时会因时区偏移越界而抛异常，必须提前短路。
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime utc)
    {
        if (utc == DateTime.MinValue)
        {
            return DateTimeOffset.MinValue;
        }

        return new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime();
    }
}
