using System.Text;
using SpaceMaid.Core.Execution;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Execution;

public class RecycleBinTargetsTests
{
    private static void WriteMetadata(string path, string originalPath, int version = 2)
    {
        var pathBytes = Encoding.Unicode.GetBytes(originalPath + "\0");
        var buffer = new byte[28 + pathBytes.Length + 8];

        BitConverter.GetBytes((long)version).CopyTo(buffer, 0);
        BitConverter.GetBytes(12345L).CopyTo(buffer, 8);                        // 文件大小
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTime()).CopyTo(buffer, 16);  // 删除时间
        BitConverter.GetBytes(pathBytes.Length).CopyTo(buffer, 24);             // 路径字节长度（版本 ≥ 2）
        pathBytes.CopyTo(buffer, 28);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, buffer);
    }

    [Fact]
    public void Should_pair_metadata_and_payload_files()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var sidDirectory = Path.Combine(systemRoot, "S-1-5-21-1");
        Directory.CreateDirectory(sidDirectory);

        var payload = Path.Combine(sidDirectory, "$RABCDEF.txt");
        File.WriteAllText(payload, "deleted content");
        WriteMetadata(Path.Combine(sidDirectory, "$IABCDEF.txt"), @"C:\Users\test\a.txt");

        var scanner = new RecycleBinTargets(new WindowsFileSystem(), new[] { new RecycleBinRoot(systemRoot, true) });
        var files = scanner.Scan(includeOtherDrives: false);

        Assert.Equal(2, files.Count);                                  // $I 与 $R 成对
        Assert.Contains(files, f => f.Path.EndsWith("$RABCDEF.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.Path.EndsWith("$IABCDEF.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new FileInfo(payload).Length, files.Single(f => f.Path.EndsWith("$RABCDEF.txt", StringComparison.OrdinalIgnoreCase)).Size);
    }

    [Fact]
    public void Should_skip_payload_without_metadata()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var sidDirectory = Path.Combine(systemRoot, "S-1-5-21-1");
        Directory.CreateDirectory(sidDirectory);
        File.WriteAllText(Path.Combine(sidDirectory, "$RORPHAN.txt"), "orphan");

        var scanner = new RecycleBinTargets(new WindowsFileSystem(), new[] { new RecycleBinRoot(systemRoot, true) });
        var files = scanner.Scan(includeOtherDrives: false);

        Assert.Empty(files);   // 看不懂的条目一律不动
    }

    [Fact]
    public void Should_skip_unparsable_metadata()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var sidDirectory = Path.Combine(systemRoot, "S-1-5-21-1");
        Directory.CreateDirectory(sidDirectory);
        File.WriteAllText(Path.Combine(sidDirectory, "$RBAD.txt"), "bad");
        File.WriteAllBytes(Path.Combine(sidDirectory, "$IBAD.txt"), new byte[] { 1, 2, 3 });   // 太短

        var scanner = new RecycleBinTargets(new WindowsFileSystem(), new[] { new RecycleBinRoot(systemRoot, true) });
        var files = scanner.Scan(includeOtherDrives: false);

        Assert.Empty(files);
    }

    [Fact]
    public void Should_only_target_system_drive_by_default()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var otherRoot = Path.Combine(root.Path, "otherroot", RecycleBinTargets.RecycleBinFolderName);

        foreach (var recycleRoot in new[] { systemRoot, otherRoot })
        {
            var sidDirectory = Path.Combine(recycleRoot, "S-1-5-21-1");
            Directory.CreateDirectory(sidDirectory);
            File.WriteAllText(Path.Combine(sidDirectory, "$R1.txt"), "x");
            WriteMetadata(Path.Combine(sidDirectory, "$I1.txt"), @"C:\Users\test\1.txt");
        }

        var roots = new[]
        {
            new RecycleBinRoot(systemRoot, IsSystemDrive: true),
            new RecycleBinRoot(otherRoot, IsSystemDrive: false)
        };
        var scanner = new RecycleBinTargets(new WindowsFileSystem(), roots);

        Assert.Equal(2, scanner.Scan(includeOtherDrives: false).Count);
        Assert.Equal(4, scanner.Scan(includeOtherDrives: true).Count);
    }

    [Fact]
    public void Should_read_original_path_from_metadata()
    {
        using var root = new TempRoot();
        var metadata = Path.Combine(root.Path, "$I1.txt");
        const string original = @"C:\Users\测试用户\Downloads\安装包 (1).exe";
        WriteMetadata(metadata, original);

        var ok = RecycleBinTargets.TryReadOriginalPath(new WindowsFileSystem(), metadata, out var parsed, out var error);

        Assert.True(ok, error);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Should_include_files_inside_deleted_folders()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var sidDirectory = Path.Combine(systemRoot, "S-1-5-21-1");
        var deletedFolder = Path.Combine(sidDirectory, "$RDIR1");
        Directory.CreateDirectory(Path.Combine(deletedFolder, "sub"));

        File.WriteAllText(Path.Combine(deletedFolder, "outer.txt"), "outer");
        File.WriteAllText(Path.Combine(deletedFolder, "sub", "inner.txt"), "inner");
        WriteMetadata(Path.Combine(sidDirectory, "$IDIR1"), @"C:\Users\test\MyFolder");

        var scanner = new RecycleBinTargets(new WindowsFileSystem(), new[] { new RecycleBinRoot(systemRoot, true) });
        var files = scanner.Scan(includeOtherDrives: false);

        // 元数据 + 目录里的两个文件（含子目录）
        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.Path.EndsWith("outer.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.Path.EndsWith("inner.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.Path.EndsWith("$IDIR1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_skip_deleted_folder_without_metadata()
    {
        using var root = new TempRoot();
        var systemRoot = Path.Combine(root.Path, "sysroot", RecycleBinTargets.RecycleBinFolderName);
        var sidDirectory = Path.Combine(systemRoot, "S-1-5-21-1");
        Directory.CreateDirectory(Path.Combine(sidDirectory, "$RORPHAN"));
        File.WriteAllText(Path.Combine(sidDirectory, "$RORPHAN", "a.txt"), "a");

        var scanner = new RecycleBinTargets(new WindowsFileSystem(), new[] { new RecycleBinRoot(systemRoot, true) });

        Assert.Empty(scanner.Scan(includeOtherDrives: false));
    }

    [Fact]
    public void Should_build_system_root_from_environment()
    {
        var roots = RecycleBinTargets.BuildRoots(new ExecutionFakeEnvironment(), new[] { @"D:\" });

        Assert.Equal(2, roots.Count);
        Assert.True(roots[0].IsSystemDrive);
        Assert.Equal(@"C:\" + RecycleBinTargets.RecycleBinFolderName, roots[0].Path);
        Assert.False(roots[1].IsSystemDrive);
        Assert.Equal(@"D:\" + RecycleBinTargets.RecycleBinFolderName, roots[1].Path);
    }
}
