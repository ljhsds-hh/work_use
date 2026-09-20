using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning.Selectors;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// `l3.duplicate-files`（重复文件）候选收窄器的用例。
/// 这里必须用**真实临时文件**：重复判定靠内容哈希，假文件系统证明不了"同大小不同内容不算重复"。
/// </summary>
public class DuplicateFileSelectorTests
{
    private static CleanItemDefinition Item() => SelectorTestData.Item(DuplicateFileSelector.ItemId);

    private static DateTimeOffset Base => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>用真实文件构造候选；同时把最后修改时间钉死，让"保留最早一份"可断言。</summary>
    private static List<ScanFile> Candidates(params (string Path, DateTimeOffset LastWrite)[] files)
    {
        foreach (var (path, lastWrite) in files)
        {
            File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
        }

        return files.Select(file => SelectorTestData.FromDisk(file.Path)).ToList();
    }

    [Fact]
    public void Should_keep_one_copy_per_group_and_list_the_rest()
    {
        using var root = new TempRoot();
        var a1 = root.WriteFile(@"dup\a1.txt", "AAAA");
        var a2 = root.WriteFile(@"dup\a2.txt", "AAAA");
        var a3 = root.WriteFile(@"dup\a3.txt", "AAAA");
        var b1 = root.WriteFile(@"dup\b1.txt", "BBBBBBBB");
        var b2 = root.WriteFile(@"dup\b2.txt", "BBBBBBBB");

        var candidates = Candidates(
            (a1, Base),
            (a2, Base.AddDays(1)),
            (a3, Base.AddDays(2)),
            (b1, Base),
            (b2, Base.AddDays(1)));

        var log = new RecordingLogSink();
        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), log);

        // 两组各保留一份（最早修改的 a1 / b1），其余 3 个列出
        Assert.Equal(3, selected.Count);
        var selectedPaths = selected.Select(file => file.Path).ToArray();
        Assert.DoesNotContain(a1, selectedPaths);
        Assert.DoesNotContain(b1, selectedPaths);
        Assert.Contains(a2, selectedPaths);
        Assert.Contains(a3, selectedPaths);
        Assert.Contains(b2, selectedPaths);
    }

    [Fact]
    public void Should_keep_the_earliest_modified_copy_even_when_its_path_sorts_last()
    {
        using var root = new TempRoot();
        var early = root.WriteFile(@"dup\zzz.txt", "SAME");
        var late = root.WriteFile(@"dup\aaa.txt", "SAME");

        var candidates = Candidates((early, Base), (late, Base.AddDays(5)));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal(late, selected[0].Path);
    }

    [Fact]
    public void Should_break_age_ties_by_path()
    {
        using var root = new TempRoot();
        var first = root.WriteFile(@"dup\a.txt", "TIE");
        var second = root.WriteFile(@"dup\b.txt", "TIE");

        var candidates = Candidates((first, Base), (second, Base));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal(second, selected[0].Path); // 同龄取路径升序的第一个（a.txt）保留，b.txt 列出
    }

    [Fact]
    public void Should_not_treat_same_size_different_content_as_duplicates()
    {
        using var root = new TempRoot();
        var a = root.WriteFile(@"dup\a.bin", "AAAA");
        var b = root.WriteFile(@"dup\b.bin", "BBBB");

        var candidates = Candidates((a, Base), (b, Base));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Empty(selected);
    }

    [Fact]
    public void Should_not_list_zero_byte_files()
    {
        using var root = new TempRoot();
        var a = root.WriteFile(@"dup\zero1.bin", string.Empty);
        var b = root.WriteFile(@"dup\zero2.bin", string.Empty);

        var candidates = Candidates((a, Base), (b, Base));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Empty(selected);
    }

    [Fact]
    public void Should_not_list_files_whose_content_cannot_be_read()
    {
        using var root = new TempRoot();
        var first = root.WriteFile(@"dup\a1.bin", "SAME-CONTENT");
        var second = root.WriteFile(@"dup\a2.bin", "SAME-CONTENT");

        // 一个不存在的路径（体积与真实文件相同），哈希读不出来 -> 必须一个都不列
        var ghost = root.Combine(@"dup\not-there.bin");
        var ghostSize = new FileInfo(first).Length;

        var candidates = Candidates((first, Base), (second, Base.AddDays(1)));
        candidates.Add(new ScanFile(ghost, ghostSize, Base, CleanActionKind.Quarantine));
        candidates.Add(new ScanFile(ghost + ".2", ghostSize, Base.AddDays(2), CleanActionKind.Quarantine));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal(second, selected[0].Path);
        Assert.DoesNotContain(selected, file => file.Path.StartsWith(ghost, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_output_sorted_by_size_descending_then_by_path()
    {
        using var root = new TempRoot();
        var smallA = root.WriteFile(@"dup\small-a.bin", "1234");
        var smallB = root.WriteFile(@"dup\small-b.bin", "1234");
        var bigA = root.WriteFile(@"dup\big-a.bin", "12345678");
        var bigB = root.WriteFile(@"dup\big-b.bin", "12345678");

        var candidates = Candidates(
            (smallA, Base),
            (smallB, Base.AddDays(1)),
            (bigA, Base),
            (bigB, Base.AddDays(1)));

        var selected = new DuplicateFileSelector().Select(Item(), candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Equal(new[] { bigB, smallB }, selected.Select(file => file.Path).ToArray());
    }

    [Fact]
    public void Should_only_handle_its_own_item()
    {
        var selector = new DuplicateFileSelector();

        Assert.True(selector.CanHandle(DuplicateFileSelector.ItemId));
        Assert.False(selector.CanHandle("l3.large-files"));
    }
}
