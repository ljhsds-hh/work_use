using System.IO;

namespace SpaceMaid.Core.Abstractions;

/// <summary>
/// 文件系统抽象（内核唯一允许直接碰磁盘的入口）。
/// 为什么需要抽象：
/// ① 扫描/隔离/执行的全部落地动作都要能注入假实现来断言"没有多删一个文件"（设计文档 D-7、§11）；
/// ② 所有"可能失败"的写操作都返回布尔 + 错误文本，禁止把 IO 异常抛穿内核。
/// </summary>
public interface IFileSystem
{
    /// <summary>文件是否存在（不抛异常）。</summary>
    bool FileExists(string path);

    /// <summary>目录是否存在（不抛异常）。</summary>
    bool DirectoryExists(string path);

    /// <summary>创建目录（含父级）；已存在时视为成功。</summary>
    void CreateDirectory(string path);

    /// <summary>文件字节数；文件不存在或读取失败返回 0。</summary>
    long GetFileSize(string path);

    /// <summary>最后写入时间；读取失败返回 <see cref="DateTimeOffset.MinValue"/>。</summary>
    DateTimeOffset GetLastWriteTime(string path);

    /// <summary>创建时间；读取失败返回 <see cref="DateTimeOffset.MinValue"/>（Windows.old 10 天窗口判定用）。</summary>
    DateTimeOffset GetCreationTime(string path);

    /// <summary>
    /// 枚举文件（只读）。
    /// 契约：目录不存在或无权限时返回**空列表**而不是抛异常，扫描引擎据此把条目标为不可用。
    /// </summary>
    /// <param name="directory">起始目录。</param>
    /// <param name="pattern">匹配模式，如 <c>*.log</c>。</param>
    /// <param name="recurse">是否递归子目录。</param>
    IReadOnlyList<string> EnumerateFiles(string directory, string pattern, bool recurse);

    /// <summary>枚举直接子目录；目录不存在或无权限时返回空列表。</summary>
    IReadOnlyList<string> EnumerateDirectories(string directory);

    /// <summary>
    /// 以只读方式打开文件供计算哈希/比对使用；调用方负责释放。
    /// 这是唯一**允许抛出**的成员：调用点（隔离区跨卷校验）必须自行捕获并记"未清理成功"。
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>尝试移动（同卷为原子重命名）。失败时返回 false 并给出中文原因，不抛异常。</summary>
    bool TryMove(string source, string destination, out string error);

    /// <summary>尝试复制。失败时返回 false 并给出中文原因，不抛异常。</summary>
    bool TryCopy(string source, string destination, out string error);

    /// <summary>
    /// 尝试删除文件。失败时返回 false 并给出中文原因，不抛异常。
    /// <para>
    /// 安全约束（I-1/D-3）：**仅 QuarantineStore（隔离区过期释放/清空）允许调用本方法**，
    /// 且必须在该文件已成功进入隔离区之后；其它任何位置调用都会被静态检索测试判红。
    /// </para>
    /// </summary>
    bool TryDeleteFile(string path, out string error);

    /// <summary>路径自身是否为重解析点（符号链接 / junction）。</summary>
    bool IsReparsePoint(string path);

    /// <summary>任一祖先目录是否为重解析点（避免"顺着链接删到别处"，设计文档 §3.2 第 6 步）。</summary>
    bool HasReparsePointAncestor(string path);

    /// <summary>文件是否被独占/被占用（落地阶段据此跳过并记原因）。</summary>
    bool IsFileLocked(string path);

    /// <summary>
    /// 计算内容哈希。
    /// <paramref name="full"/> = true 时做全文件 SHA-256；false 时只读首尾各 4MB（含文件长度）做快速分组，
    /// 用于重复文件扫描的第一轮筛选（设计文档 §5.2）。
    /// </summary>
    /// <param name="path">目标文件。</param>
    /// <param name="full">是否全量校验。</param>
    /// <returns>哈希文本；读取失败返回空串。</returns>
    string ComputeHash(string path, bool full);
}
