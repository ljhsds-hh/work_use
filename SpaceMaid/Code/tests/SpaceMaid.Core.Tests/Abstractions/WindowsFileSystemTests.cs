using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Platform;

namespace SpaceMaid.Core.Tests.Abstractions;

/// <summary>
/// Task 2 Step 1：文件系统抽象的真实实现契约。
/// 所有 IO 都发生在 <c>%TEMP%\spacemaid-tests-&lt;guid&gt;</c> 内，绝不触碰真实系统目录。
/// </summary>
public class WindowsFileSystemTests
{
    [Fact]
    public void Should_report_size_and_creation_flags_for_plain_file()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string file = root.WriteFile(@"level1\sample.txt", "hello spacemaid");

        Assert.True(fs.FileExists(file));
        Assert.Equal(15, fs.GetFileSize(file));
        Assert.False(fs.IsReparsePoint(file));

        fs.CreateDirectory(root.Combine("level1", "level2"));
        Assert.True(fs.DirectoryExists(root.Combine("level1", "level2")));

        string createdDir = root.Combine("level1");
        Assert.False(fs.IsReparsePoint(createdDir));
        Assert.False(fs.HasReparsePointAncestor(createdDir));
    }

    [Fact]
    public void EnumerateFiles_should_support_pattern_and_recursion()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        root.WriteFile(@"a.log", "1");
        root.WriteFile(@"nested\b.log", "2");
        root.WriteFile(@"nested\c.txt", "3");

        IReadOnlyList<string> top = fs.EnumerateFiles(root.Path, "*.log", recurse: false);
        Assert.Single(top);

        IReadOnlyList<string> all = fs.EnumerateFiles(root.Path, "*.log", recurse: true);
        Assert.Equal(2, all.Count);

        IReadOnlyList<string> dirs = fs.EnumerateDirectories(root.Path);
        Assert.Contains(dirs, d => string.Equals(d, root.Combine("nested"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateFiles_should_return_empty_for_missing_directory_instead_of_throwing()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        Assert.Empty(fs.EnumerateFiles(root.Combine("not-there"), "*", recurse: true));
        Assert.Empty(fs.EnumerateDirectories(root.Combine("not-there")));
    }

    [Fact]
    public void OpenRead_should_allow_reading_whole_content()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string file = root.WriteFile("read.txt", "abcdef");

        using Stream stream = fs.OpenRead(file);
        using StreamReader reader = new StreamReader(stream);
        Assert.Equal("abcdef", reader.ReadToEnd());
    }

    [Fact]
    public void TryMove_should_move_file_without_losing_content()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string source = root.WriteFile("src.bin", "payload");
        string destination = root.Combine("moved.bin");

        bool ok = fs.TryMove(source, destination, out string error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.False(fs.FileExists(source));
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Fact]
    public void TryMove_should_report_error_instead_of_throwing()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string missing = root.Combine("missing.bin");
        bool ok = fs.TryMove(missing, root.Combine("x.bin"), out string error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryCopy_should_copy_file()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string source = root.WriteFile("a.txt", "copy-me");
        string destination = root.Combine("b.txt");

        Assert.True(fs.TryCopy(source, destination, out string error));
        Assert.Equal(string.Empty, error);
        Assert.Equal("copy-me", File.ReadAllText(destination));
        Assert.True(fs.FileExists(source));
    }

    [Fact]
    public void IsFileLocked_should_detect_exclusive_holder()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string file = root.WriteFile("locked.txt", "busy");
        Assert.False(fs.IsFileLocked(file));

        using FileStream holder = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(fs.IsFileLocked(file));
    }

    [Fact]
    public void ComputeHash_should_be_stable_and_full_hash_should_cover_whole_file()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string a = root.WriteFile("h1.txt", "same-content");
        string b = root.WriteFile("h2.txt", "same-content");
        string c = root.WriteFile("h3.txt", "other-content");

        Assert.Equal(fs.ComputeHash(a, full: false), fs.ComputeHash(b, full: false));
        Assert.Equal(fs.ComputeHash(a, full: true), fs.ComputeHash(b, full: true));
        Assert.NotEqual(fs.ComputeHash(a, full: true), fs.ComputeHash(c, full: true));

        // 采样哈希（full=false）必须把文件长度混入，避免"首尾相同但中间不同"的文件被误判为重复。
        string longA = root.WriteFile("long-a.bin", new string('x', 1024));
        string longB = root.WriteFile("long-b.bin", new string('x', 2048));
        Assert.NotEqual(fs.ComputeHash(longA, full: false), fs.ComputeHash(longB, full: false));
    }

    [Fact]
    public void GetLastWriteTime_and_GetCreationTime_should_reflect_real_timestamps()
    {
        using TempRoot root = new TempRoot();
        IFileSystem fs = new WindowsFileSystem();

        string file = root.WriteFile("time.txt", "t");
        DateTime expected = new DateTime(2024, 3, 1, 8, 30, 0, DateTimeKind.Local);
        File.SetLastWriteTime(file, expected);
        File.SetCreationTime(file, expected);

        Assert.Equal(expected, fs.GetLastWriteTime(file).LocalDateTime, TimeSpan.FromSeconds(2));
        Assert.Equal(expected, fs.GetCreationTime(file).LocalDateTime, TimeSpan.FromSeconds(2));
    }
}
