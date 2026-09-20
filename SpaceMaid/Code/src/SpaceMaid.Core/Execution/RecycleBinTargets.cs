using System.Text;
using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Logging;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Scanning;

namespace SpaceMaid.Core.Execution;

/// <summary>一个回收站根的描述。</summary>
/// <param name="Path">例如 <c>C:\$Recycle.Bin</c>。</param>
/// <param name="IsSystemDrive">是否系统盘（默认只处理系统盘，其他盘需用户在设置里显式启用，需求 2.5）。</param>
public sealed record RecycleBinRoot(string Path, bool IsSystemDrive);

/// <summary>
/// 回收站扫描（需求 2.5）。
///
/// 实现要点：
/// 1. **默认只处理系统盘**的 <c>$Recycle.Bin</c>；
/// 2. 回收站内部是按 SID 分目录、成对存放 <c>$I*</c>（元数据：原始路径与删除时间）与 <c>$R*</c>（实体文件）；
/// 3. **成对处理**：把 <c>$I</c> 与 <c>$R</c> 都列为待隔离文件，还原时两者一起搬回去，回收站条目才不会残缺；
/// 4. 解析不了 <c>$I</c> 的条目（没有元数据的孤儿 <c>$R</c>）**一律跳过**——看不懂的东西不动它。
/// </summary>
public sealed class RecycleBinTargets : IRecycleBinScanner
{
    /// <summary>回收站目录名（系统盘根下）。</summary>
    public const string RecycleBinFolderName = "$Recycle.Bin";

    private readonly IFileSystem _fileSystem;
    private readonly IReadOnlyList<RecycleBinRoot> _roots;
    private readonly ILogSink _log;

    public RecycleBinTargets(IFileSystem fileSystem, IReadOnlyList<RecycleBinRoot> roots, ILogSink? log = null)
    {
        _fileSystem = fileSystem;
        _roots = roots;
        _log = log ?? SilentLogSink.Instance;
    }

    /// <summary>按环境构造默认根列表：系统盘必选，其他盘由参数决定。</summary>
    public static IReadOnlyList<RecycleBinRoot> BuildRoots(IEnvironmentProbe environment, IReadOnlyList<string>? otherDriveRoots = null)
    {
        var roots = new List<RecycleBinRoot>
        {
            new(Path.Combine(environment.SystemDrive + Path.DirectorySeparatorChar, RecycleBinFolderName), true)
        };

        if (otherDriveRoots is not null)
        {
            roots.AddRange(otherDriveRoots.Select(root => new RecycleBinRoot(
                Path.Combine(root, RecycleBinFolderName),
                IsSystemDrive: false)));
        }

        return roots;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScanFile> Scan(bool includeOtherDrives)
    {
        var result = new List<ScanFile>();

        foreach (var root in _roots)
        {
            if (!root.IsSystemDrive && !includeOtherDrives)
            {
                continue;
            }

            if (!_fileSystem.DirectoryExists(root.Path))
            {
                continue;
            }

            foreach (var sidDirectory in _fileSystem.EnumerateDirectories(root.Path))
            {
                result.AddRange(ScanSidDirectory(sidDirectory));
            }
        }

        return result;
    }

    /// <summary>
    /// 解析 <c>$I</c> 元数据里的原始路径（供清单展示"这个文件原本在哪"）。
    /// 格式：8 字节版本 + 8 字节文件大小 + 8 字节删除时间；版本 ≥ 2 时第 24 字节起是 4 字节路径字节长度，
    /// 路径本体是从 28（或版本 1 的 24）开始的 UTF-16LE 字符串，以 \0 结尾。
    /// </summary>
    public static bool TryReadOriginalPath(IFileSystem fileSystem, string metadataFilePath, out string originalPath, out string error)
    {
        originalPath = string.Empty;
        error = string.Empty;

        try
        {
            using var stream = fileSystem.OpenRead(metadataFilePath);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var buffer = memory.ToArray();

            if (buffer.Length < 24)
            {
                error = "$I 元数据过短";
                return false;
            }

            var version = BitConverter.ToInt64(buffer, 0);
            int pathOffset;
            int pathByteLength;

            if (version >= 2)
            {
                if (buffer.Length < 28)
                {
                    error = "$I 元数据缺少路径长度字段";
                    return false;
                }

                pathByteLength = BitConverter.ToInt32(buffer, 24);
                pathOffset = 28;
            }
            else
            {
                pathByteLength = -1;
                pathOffset = 24;
            }

            var available = buffer.Length - pathOffset;
            if (available <= 0)
            {
                error = "$I 元数据没有路径内容";
                return false;
            }

            var length = pathByteLength > 0 ? Math.Min(pathByteLength, available) : available;
            var text = Encoding.Unicode.GetString(buffer, pathOffset, length).TrimEnd('\0', ' ');

            if (string.IsNullOrWhiteSpace(text) || (!text.Contains(':') && !text.Contains(Path.DirectorySeparatorChar)))
            {
                error = "$I 元数据里的原始路径不可识别";
                return false;
            }

            originalPath = text;
            return true;
        }
        catch (Exception ex)
        {
            error = $"读取 $I 元数据失败：{ex.Message}";
            return false;
        }
    }

    private IEnumerable<ScanFile> ScanSidDirectory(string sidDirectory)
    {
        var files = _fileSystem.EnumerateFiles(sidDirectory, "*", recurse: false);
        var metadata = files.Where(f => Path.GetFileName(f).StartsWith("$I", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var payload in files.Where(f => Path.GetFileName(f).StartsWith("$R", StringComparison.OrdinalIgnoreCase)))
        {
            var pair = ResolvePair(payload, metadata);
            if (pair is null)
            {
                continue;
            }

            // 元数据与实体文件都要搬走，还原时才能成对放回
            yield return ToScanFile(pair.Value.MetadataPath);
            yield return ToScanFile(payload);
        }

        // 被删除的**文件夹**在回收站里同样是一对 $I/$R，只不过 $R 是目录而不是文件
        // （对抗式评审 F-17：此前只枚举文件，导致"回收站里被删掉的文件夹"整类被忽略）。
        // 这里把目录里的文件逐个列出——执行器只搬文件，空目录留在回收站里无害。
        foreach (var payloadDirectory in _fileSystem.EnumerateDirectories(sidDirectory))
        {
            var name = Path.GetFileName(payloadDirectory);
            if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pair = ResolvePair(payloadDirectory, metadata);
            if (pair is null)
            {
                continue;
            }

            _log.Info($"回收站条目（文件夹）：{pair.Value.OriginalPath} -> {payloadDirectory}");

            yield return ToScanFile(pair.Value.MetadataPath);

            foreach (var file in _fileSystem.EnumerateFiles(payloadDirectory, "*", recurse: true))
            {
                yield return ToScanFile(file);
            }
        }
    }

    /// <summary>
    /// 把 <c>$R&lt;后缀&gt;</c> 与其配对的 <c>$I&lt;后缀&gt;</c> 元数据对上；对不上或元数据读不出来时返回 null
    /// （看不懂的条目一律不动）。
    /// </summary>
    private (string MetadataPath, string OriginalPath)? ResolvePair(string payloadPath, List<string> metadataFiles)
    {
        var suffix = Path.GetFileName(payloadPath)[2..];
        var metadataPath = metadataFiles.FirstOrDefault(candidate =>
            Path.GetFileName(candidate)[2..].Equals(suffix, StringComparison.OrdinalIgnoreCase));

        if (metadataPath is null)
        {
            _log.Warn($"回收站条目缺少 $I 元数据，已跳过：{payloadPath}");
            return null;
        }

        if (!TryReadOriginalPath(_fileSystem, metadataPath, out var originalPath, out var error))
        {
            _log.Warn($"回收站条目元数据无法解析，已跳过：{metadataPath}（{error}）");
            return null;
        }

        return (metadataPath, originalPath);
    }

    private ScanFile ToScanFile(string path) =>
        new(path, _fileSystem.GetFileSize(path), _fileSystem.GetLastWriteTime(path), CleanActionKind.Quarantine);
}
