using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>
/// 分类树构建：自定义子分组必须可见、父子计数必须一致（旧实现在这里漏过）。
/// </summary>
public class CommandTreeBuilderTests
{
    private static CommandEntry E(string id, string category, string group)
        => new() { Id = id, Title = id, Command = "echo " + id, Category = category, Group = group };

    [Fact]
    public void 根节点计数等于全部条目数()
    {
        var root = CommandTreeBuilder.BuildRoot(
        [
            E("1", "PowerShell", "文件与目录"),
            E("2", "Git", "基础操作"),
            E("3", "我的分类", "我的分组"),
        ]);

        Assert.Equal(CommandTreeBuilder.AllLabel, root.Label);
        Assert.Equal(3, root.Count);
    }

    [Fact]
    public void 自定义子分组进入树且可筛选()
    {
        var entries = new List<CommandEntry>
        {
            E("1", "PowerShell", "文件与目录"),
            E("2", "PowerShell", "我的偷懒分组"),
        };

        var root = CommandTreeBuilder.BuildRoot(entries);
        var ps = root.Children.Single(n => n.Category == "PowerShell");
        var custom = ps.Children.Single(n => n.Group == "我的偷懒分组");

        Assert.Equal(1, custom.Count);
        // 点得进去：按该节点过滤能拿到条目
        var matched = CommandSearch.Filter(entries, null, custom.Category, custom.Group);
        Assert.Equal("2", Assert.Single(matched).Id);
    }

    [Fact]
    public void 预置分组顺序不变且空分组保留()
    {
        var root = CommandTreeBuilder.BuildRoot([E("1", "Git", "暂存 stash")]);
        var git = root.Children.Single(n => n.Category == "Git");

        // 预置 10 个分组全部保留（空分组角标隐藏但可点进去看到空态）
        Assert.Equal(Catalog.GroupsOf("Git").Count, git.Children.Count);
        Assert.Equal(1, git.Children.Single(n => n.Group == "暂存 stash").Count);
        Assert.All(git.Children.Where(n => n.Group != "暂存 stash"), n => Assert.Equal(0, n.Count));
        Assert.Equal(new[] { "配置与初始化", "基础操作", "分支操作" }, git.Children.Take(3).Select(n => n.Label));
    }

    [Fact]
    public void 自定义分组排在预置之后并按名称排序()
    {
        var root = CommandTreeBuilder.BuildRoot(
        [
            E("1", "Git", "乙分组"),
            E("2", "Git", "甲分组"),
        ]);

        var git = root.Children.Single(n => n.Category == "Git");
        var labels = git.Children.Select(n => n.Label).ToList();

        Assert.Equal("甲分组", labels[^2]);
        Assert.Equal("乙分组", labels[^1]);
    }

    [Fact]
    public void 未分组条目落到未分组节点()
    {
        var entries = new List<CommandEntry> { E("1", "PowerShell", ""), E("2", "PowerShell", "系统信息") };
        var root = CommandTreeBuilder.BuildRoot(entries);
        var ps = root.Children.Single(n => n.Category == "PowerShell");

        var ungrouped = ps.Children.Single(n => n.Label == CommandTreeBuilder.UngroupedLabel);
        Assert.Equal("", ungrouped.Group);
        Assert.Equal(1, ungrouped.Count);

        var matched = CommandSearch.Filter(entries, null, ungrouped.Category, ungrouped.Group);
        Assert.Equal("1", Assert.Single(matched).Id);
    }

    [Fact]
    public void 未知分类追加节点且条目不丢()
    {
        var root = CommandTreeBuilder.BuildRoot([E("1", "Docker", "容器")]);

        var docker = root.Children.Single(n => n.Category == "Docker");
        Assert.Equal(1, docker.Count);
        Assert.Equal("容器", Assert.Single(docker.Children).Label);
    }

    [Fact]
    public void 任一节点的计数等于其子节点计数之和()
    {
        var root = CommandTreeBuilder.BuildRoot(
        [
            E("1", "PowerShell", "文件与目录"),
            E("2", "PowerShell", "文件与目录"),
            E("3", "PowerShell", "我自己加的分组"),
            E("4", "PowerShell", ""),
            E("5", "Git", "基础操作"),
            E("6", "Docker", "容器"),
        ]);

        AssertConsistent(root);
    }

    private static void AssertConsistent(FilterNode node)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        Assert.Equal(node.Children.Sum(c => c.Count), node.Count);
        foreach (var child in node.Children)
        {
            AssertConsistent(child);
        }
    }
}
