using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>搜索过滤与排序逻辑（需求 4.2 / 4.4）。</summary>
public class CommandSearchTests
{
    private static List<CommandEntry> Sample() =>
    [
        new() { Id = "1", Title = "撤销未推送的提交", Command = "git reset --hard origin/main", Note = "丢弃本地未提交修改", Category = "Git", Group = "撤销回退" },
        new() { Id = "2", Title = "查看端口占用", Command = "Get-NetTCPConnection -LocalPort <端口号>", Note = "排查端口被占用", Category = "PowerShell", Group = "进程与服务" },
        new() { Id = "3", Title = "压缩目录为 zip", Command = "Compress-Archive -Path <源目录>", Note = "", Category = "PowerShell", Group = "压缩与归档" },
    ];

    [Fact]
    public void 无搜索词_按分类分组过滤()
    {
        var all = CommandSearch.Filter(Sample(), null);
        Assert.Equal(3, all.Count);

        var git = CommandSearch.Filter(Sample(), null, category: "Git");
        Assert.Single(git);
        Assert.Equal("1", git[0].Id);

        var group = CommandSearch.Filter(Sample(), null, category: "PowerShell", group: "压缩与归档");
        Assert.Single(group);
        Assert.Equal("3", group[0].Id);
    }

    [Fact]
    public void 有搜索词_跨分类全库匹配()
    {
        // 命中标题
        var byTitle = CommandSearch.Filter(Sample(), "端口", category: "Git");
        Assert.Single(byTitle);
        Assert.Equal("2", byTitle[0].Id);

        // 命中备注
        var byNote = CommandSearch.Filter(Sample(), "丢弃本地");
        Assert.Single(byNote);
        Assert.Equal("1", byNote[0].Id);

        // 命中命令内容
        var byCommand = CommandSearch.Filter(Sample(), "Compress");
        Assert.Single(byCommand);
        Assert.Equal("3", byCommand[0].Id);
    }

    [Fact]
    public void 多关键词_AND_组合_忽略大小写()
    {
        var hits = CommandSearch.Filter(Sample(), "端口 排查");
        Assert.Single(hits);
        Assert.Equal("2", hits[0].Id);

        var none = CommandSearch.Filter(Sample(), "端口 reset");
        Assert.Empty(none);

        var lower = CommandSearch.Filter(Sample(), "compress-archive");
        Assert.Single(lower);
        Assert.Equal("3", lower[0].Id);
    }

    [Fact]
    public void 最近使用排序_未使用的排后面()
    {
        var entries = Sample();
        entries[2].LastUsedAt = new DateTime(2026, 9, 1);
        entries[0].LastUsedAt = new DateTime(2026, 9, 10);
        // entries[1] 从未使用

        var sorted = CommandSearch.Filter(entries, null, sort: SortMode.RecentlyUsed);
        // 1（9/10）> 3（9/1）> 2（从未使用）
        Assert.Equal(["1", "3", "2"], sorted.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void 使用最多排序()
    {
        var entries = Sample();
        entries[0].UseCount = 5;
        entries[2].UseCount = 12;

        var sorted = CommandSearch.Filter(entries, null, sort: SortMode.MostUsed);
        Assert.Equal(["3", "1", "2"], sorted.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void 默认排序_按分类定义顺序与分组定义顺序()
    {
        var sorted = CommandSearch.Filter(Sample(), null);
        // PowerShell 在前（目录序），Git 在后
        Assert.Equal("2", sorted[0].Id);
        Assert.Equal("3", sorted[1].Id);
        Assert.Equal("1", sorted[2].Id);
    }

    [Fact]
    public void 空字符串分组等于筛选未分组条目()
    {
        var entries = Sample();
        entries.Add(new() { Id = "4", Title = "没写分组的命令", Command = "echo", Note = "", Category = "PowerShell", Group = "" });

        // null = 不限定分组；"" = 只筛未分组条目（左侧树的兜底节点）
        Assert.Equal(4, CommandSearch.Filter(entries, null).Count);
        var ungrouped = CommandSearch.Filter(entries, null, category: "PowerShell", group: "");
        Assert.Equal("4", Assert.Single(ungrouped).Id);
    }
}
