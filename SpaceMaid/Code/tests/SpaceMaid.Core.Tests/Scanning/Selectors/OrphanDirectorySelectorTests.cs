using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning.Selectors;
using SpaceMaid.Core.Tests.Abstractions;

namespace SpaceMaid.Core.Tests.Scanning.Selectors;

/// <summary>
/// `l3.orphan-app-dirs`（卸载残留目录）候选收窄器的用例。
///
/// 这一项的口径是"四条同时满足才列出，任一条件无法判定就不列出"，
/// 所以每个否决条件都要有独立用例，且"抛异常 = 不列出"必须有专门证据。
/// </summary>
public class OrphanDirectorySelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));

    private static DateTimeOffset Stale => Now - TimeSpan.FromDays(200);

    private static DateTimeOffset Fresh => Now - TimeSpan.FromDays(10);

    [Fact]
    public void Should_list_only_directories_meeting_all_four_conditions()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");

        var orphan = root.WriteFile(@"ProgramData\OldApp\older.dat", "old");
        var fresh = root.WriteFile(@"ProgramData\NewApp\fresh.dat", "new");
        var referenced = root.WriteFile(@"ProgramData\RefApp\ref.dat", "ref");
        var unknown = root.WriteFile(@"ProgramData\UnknownApp\unk.dat", "unk");
        var loose = root.WriteFile(@"ProgramData\loose.dat", "loose");
        Directory.CreateDirectory(root.Combine("ProgramData", "EmptyApp"));

        var candidates = new List<ScanFile>
        {
            SelectorTestData.File(orphan, 1024, Stale),
            SelectorTestData.File(fresh, 1024, Fresh),
            SelectorTestData.File(referenced, 1024, Stale),
            SelectorTestData.File(unknown, 1024, Stale),
            SelectorTestData.File(loose, 1024, Stale)
        };

        var index = new FakeInstalledProgramIndex();
        index.Referenced.Add(Path.Combine(programData, "RefApp"));
        index.ThrowFor.Add(Path.Combine(programData, "UnknownApp"));

        var selector = new OrphanDirectorySelector(index, new FakeClock(Now), new RecordingLogSink());
        var item = SelectorTestData.Item(OrphanDirectorySelector.ItemId, TargetRule.Contents(programData));

        var selected = selector.Select(item, candidates, new WindowsFileSystem(), new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal(orphan, selected[0].Path);

        // 空目录根本不产生候选，这里显式确认它不在结果里
        Assert.DoesNotContain(selected, file => file.Path.Contains("EmptyApp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_not_list_a_directory_referenced_by_the_registry()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var referenced = root.WriteFile(@"ProgramData\InstalledApp\a.dat", "x");

        var index = new FakeInstalledProgramIndex();
        index.Referenced.Add(Path.Combine(programData, "InstalledApp"));

        var selected = Build(programData, index, candidates: new[] { SelectorTestData.File(referenced, 1024, Stale) });

        Assert.Empty(selected);
        Assert.Contains(Path.Combine(programData, "InstalledApp"), index.Queried);
    }

    [Fact]
    public void Should_not_list_a_directory_modified_within_the_staleness_window()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var freshA = root.WriteFile(@"ProgramData\RecentApp\a.dat", "x");
        var freshB = root.WriteFile(@"ProgramData\RecentApp\b.dat", "y");

        var index = new FakeInstalledProgramIndex();

        var selected = Build(
            programData,
            index,
            candidates: new[]
            {
                SelectorTestData.File(freshA, 1024, Stale),
                SelectorTestData.File(freshB, 1024, Fresh) // 只要有一个近期改过，整组不列出
            });

        Assert.Empty(selected);
    }

    [Fact]
    public void Should_not_list_directories_that_have_no_files()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        Directory.CreateDirectory(root.Combine("ProgramData", "EmptyApp"));

        var index = new FakeInstalledProgramIndex();

        var selected = Build(programData, index, candidates: Array.Empty<ScanFile>());

        Assert.Empty(selected);
        Assert.True(Directory.Exists(Path.Combine(programData, "EmptyApp")));
    }

    [Fact]
    public void Should_treat_a_failing_registry_lookup_as_referenced()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var file = root.WriteFile(@"ProgramData\BrokenLookup\a.dat", "x");

        var index = new FakeInstalledProgramIndex();
        index.ThrowFor.Add(Path.Combine(programData, "BrokenLookup"));

        var log = new RecordingLogSink();
        var selected = Build(
            programData,
            index,
            candidates: new[] { SelectorTestData.File(file, 1024, Stale) },
            log: log);

        Assert.Empty(selected);
        Assert.Contains(log.Warnings, message => message.Contains("被引用", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_not_list_a_candidate_that_is_not_under_any_known_root()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var outside = root.WriteFile(@"Elsewhere\SomeApp\a.dat", "x");

        var index = new FakeInstalledProgramIndex();

        var selected = Build(programData, index, candidates: new[] { SelectorTestData.File(outside, 1024, Stale) });

        Assert.Empty(selected);
        Assert.Empty(index.Queried); // 归不到根下 -> 连问都不问
    }

    [Fact]
    public void Should_skip_when_the_root_cannot_be_resolved()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var file = root.WriteFile(@"ProgramData\SomeApp\a.dat", "x");

        var index = new FakeInstalledProgramIndex();
        var log = new RecordingLogSink();

        // 条目路径里带着没展开的变量：根目录确定不了 -> 整项不列出
        var selector = new OrphanDirectorySelector(index, new FakeClock(Now), log);
        var item = SelectorTestData.Item(
            OrphanDirectorySelector.ItemId,
            TargetRule.Contents(@"%FAKEDATA%"));

        var selected = selector.Select(
            item,
            new[] { SelectorTestData.File(file, 1024, Stale) },
            new WindowsFileSystem(),
            log);

        Assert.Empty(selected);
        Assert.Contains(log.Warnings, message => message.Contains("根目录无法确定", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_expand_environment_variables_when_resolving_roots()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var file = root.WriteFile(@"ProgramData\OldApp\a.dat", "x");

        var index = new FakeInstalledProgramIndex();
        var selector = new OrphanDirectorySelector(index, new FakeClock(Now), new RecordingLogSink(), new FakeEnvironmentProbe(programData));
        var item = SelectorTestData.Item(OrphanDirectorySelector.ItemId, TargetRule.Contents(@"%FAKEDATA%"));

        var selected = selector.Select(
            item,
            new[] { SelectorTestData.File(file, 1024, Stale) },
            new WindowsFileSystem(),
            new RecordingLogSink());

        Assert.Single(selected);
        Assert.Equal(file, selected[0].Path);
    }

    [Fact]
    public void Should_use_the_deepest_matching_root()
    {
        using var root = new TempRoot();
        var programData = root.Combine("ProgramData");
        var nestedRoot = Path.Combine(programData, "Nested");
        var file = root.WriteFile(@"ProgramData\Nested\OldApp\a.dat", "x");

        var index = new FakeInstalledProgramIndex();
        var selector = new OrphanDirectorySelector(index, new FakeClock(Now), new RecordingLogSink());
        var item = SelectorTestData.Item(
            OrphanDirectorySelector.ItemId,
            TargetRule.Contents(programData),
            TargetRule.Contents(nestedRoot));

        var selected = selector.Select(
            item,
            new[] { SelectorTestData.File(file, 1024, Stale) },
            new WindowsFileSystem(),
            new RecordingLogSink());

        // 最深根是 ...\ProgramData\Nested -> 第一层子目录是 ...\Nested\OldApp
        Assert.Single(selected);
        Assert.Equal(Path.Combine(nestedRoot, "OldApp"), index.Queried.Single());
    }

    [Fact]
    public void Should_only_handle_its_own_item()
    {
        var selector = new OrphanDirectorySelector(new FakeInstalledProgramIndex(), new FakeClock(Now));

        Assert.True(selector.CanHandle(OrphanDirectorySelector.ItemId));
        Assert.False(selector.CanHandle("l3.large-files"));
    }

    private static IReadOnlyList<ScanFile> Build(
        string programData,
        FakeInstalledProgramIndex index,
        IReadOnlyList<ScanFile> candidates,
        RecordingLogSink? log = null)
    {
        var sink = log ?? new RecordingLogSink();
        var selector = new OrphanDirectorySelector(index, new FakeClock(Now), sink);
        var item = SelectorTestData.Item(OrphanDirectorySelector.ItemId, TargetRule.Contents(programData));

        return selector.Select(item, candidates, new WindowsFileSystem(), sink);
    }
}
