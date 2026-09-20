using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>常用 / 最近榜单：只收用过的条目，排序要稳定可比。</summary>
public class CommandRankingTests
{
    private static CommandEntry E(string id, int useCount, DateTime? lastUsed, string title = "")
        => new() { Id = id, Title = title.Length > 0 ? title : id, Command = "echo " + id, Category = "Git", Group = "基础操作", UseCount = useCount, LastUsedAt = lastUsed };

    [Fact]
    public void 常用榜_按次数降序_没用过的不上榜()
    {
        var entries = new List<CommandEntry>
        {
            E("never", 0, null),
            E("often", 7, new DateTime(2026, 9, 1)),
            E("twice", 2, new DateTime(2026, 9, 10)),
        };

        var top = CommandRanking.TopUsed(entries);

        Assert.Equal(["often", "twice"], top.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void 常用榜_次数相同时最近使用的在前()
    {
        var entries = new List<CommandEntry>
        {
            E("older", 3, new DateTime(2026, 9, 1)),
            E("newer", 3, new DateTime(2026, 9, 20)),
        };

        Assert.Equal(["newer", "older"], CommandRanking.TopUsed(entries).Select(e => e.Id).ToArray());
    }

    [Fact]
    public void 最近榜_按最近使用时间降序()
    {
        var entries = new List<CommandEntry>
        {
            E("never", 0, null),
            E("older", 5, new DateTime(2026, 9, 1)),
            E("newer", 1, new DateTime(2026, 9, 20)),
        };

        Assert.Equal(["newer", "older"], CommandRanking.TopRecent(entries).Select(e => e.Id).ToArray());
    }

    [Fact]
    public void 榜单数量受上限约束()
    {
        var entries = Enumerable.Range(0, 25)
            .Select(i => E($"c{i}", i + 1, new DateTime(2026, 9, 1)))
            .ToList();

        var top = CommandRanking.TopUsed(entries);

        Assert.Equal(CommandRanking.DefaultSize, top.Count);
        Assert.Equal("c24", top[0].Id);
    }

    [Fact]
    public void 个数传零或负数时返回空()
    {
        var entries = new List<CommandEntry> { E("a", 1, DateTime.Now) };

        Assert.Empty(CommandRanking.TopUsed(entries, 0));
        Assert.Empty(CommandRanking.TopUsed(entries, -5));
    }

    [Fact]
    public void 全都没用过时榜单为空()
    {
        Assert.Empty(CommandRanking.TopUsed([E("a", 0, null)]));
        Assert.Empty(CommandRanking.TopRecent([E("a", 0, null)]));
    }
}
