using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>导入合并：新条目追加、重复跳过、同 Id 更新描述但保留本机使用统计。</summary>
public class LibraryMergerTests
{
    private static CommandEntry E(string id, string title, string command,
        string category = "Git", string group = "基础操作", string note = "", int useCount = 0, DateTime? lastUsed = null)
        => new()
        {
            Id = id,
            Title = title,
            Command = command,
            Note = note,
            Category = category,
            Group = group,
            UseCount = useCount,
            LastUsedAt = lastUsed,
        };

    [Fact]
    public void 新条目追加在现有之后()
    {
        var existing = new List<CommandEntry> { E("a", "看状态", "git status") };
        var incoming = new List<CommandEntry> { E("b", "看日志", "git log"), E("c", "看分支", "git branch") };

        var result = LibraryMerger.Merge(existing, incoming);

        Assert.Equal(["a", "b", "c"], result.Commands.Select(c => c.Id).ToArray());
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void 内容重复的条目跳过()
    {
        var existing = new List<CommandEntry> { E("a", "看状态", "git status -sb") };
        var incoming = new List<CommandEntry>
        {
            // Id 不同、大小写与首尾空白不同、换行风格不同 → 都算同一条
            E("other", " 看状态 ", "git status -sb\r\n"),
            E("b", "看日志", "git log"),
        };

        var result = LibraryMerger.Merge(existing, incoming);

        Assert.Equal(["a", "b"], result.Commands.Select(c => c.Id).ToArray());
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void 同_Id_不同内容_更新描述但保留本机使用统计()
    {
        var used = new DateTime(2026, 9, 1, 8, 0, 0);
        var existing = new List<CommandEntry> { E("a", "看状态", "git status", note: "旧备注", useCount: 7, lastUsed: used) };
        var incoming = new List<CommandEntry> { E("a", "查看工作区状态", "git status -sb", note: "新备注") };

        var result = LibraryMerger.Merge(existing, incoming);

        var merged = Assert.Single(result.Commands);
        Assert.Equal("a", merged.Id);
        Assert.Equal("查看工作区状态", merged.Title);
        Assert.Equal("git status -sb", merged.Command);
        Assert.Equal("新备注", merged.Note);
        Assert.Equal(7, merged.UseCount);           // 本机统计留着
        Assert.Equal(used, merged.LastUsedAt);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Added);
    }

    [Fact]
    public void 同_Id_内容也相同_算重复()
    {
        var existing = new List<CommandEntry> { E("a", "看状态", "git status") };

        var result = LibraryMerger.Merge(existing, [E("a", "看状态", "git status")]);

        Assert.Single(result.Commands);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Updated);
    }

    [Fact]
    public void 命令本身区分大小写()
    {
        var existing = new List<CommandEntry> { E("a", "删", "git clean -fd") };

        var result = LibraryMerger.Merge(existing, [E("b", "删", "git clean -FD")]);

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public void 现有或导入为空都能处理()
    {
        var onlyIncoming = LibraryMerger.Merge([], [E("a", "看状态", "git status")]);
        Assert.Equal(1, onlyIncoming.Added);

        var onlyExisting = LibraryMerger.Merge([E("a", "看状态", "git status")], []);
        Assert.Single(onlyExisting.Commands);
        Assert.Equal(0, onlyExisting.Added);
        Assert.Equal(0, onlyExisting.Skipped);
    }

    [Fact]
    public void 导入侧自己重复也只加一次()
    {
        var result = LibraryMerger.Merge([], [E("x", "看状态", "git status"), E("y", "看状态", "git status")]);

        Assert.Single(result.Commands);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void 分类分组不一致不算重复()
    {
        var existing = new List<CommandEntry> { E("a", "看状态", "git status", "Git", "基础操作") };

        var result = LibraryMerger.Merge(existing, [E("b", "看状态", "git status", "Git", "查看历史")]);

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(1, result.Added);
    }
}
