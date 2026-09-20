using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>导入数据校验：不合规条目跳过、Id 缺失或重复自动修正。</summary>
public class LibraryValidatorTests
{
    private static CommandEntry E(string id, string title, string command, string category = "Git", string group = "基础操作")
        => new() { Id = id, Title = title, Command = command, Category = category, Group = group };

    [Fact]
    public void 必填字段齐全的条目保留()
    {
        var (valid, problems) = LibraryValidator.Validate([E("1", "看状态", "git status -sb")]);

        Assert.Single(valid);
        Assert.Empty(problems);
    }

    [Fact]
    public void 缺字段的条目被跳过并给出原因()
    {
        var (valid, problems) = LibraryValidator.Validate(
        [
            E("1", "看状态", "git status -sb"),
            E("2", "", "git log"),
            E("3", "空命令", "  "),
            new CommandEntry { Id = "4", Title = "无分类", Command = "echo", Category = "", Group = "基础操作" },
            new CommandEntry { Id = "5", Title = "无分组", Command = "echo", Category = "Git", Group = "" },
        ]);

        Assert.Single(valid);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void 自定义子分组不算不合规()
    {
        var (valid, problems) = LibraryValidator.Validate([E("1", "我的命令", "echo hi", "Git", "我自己加的分组")]);

        Assert.Single(valid);
        Assert.Empty(problems);
    }

    [Fact]
    public void Id_缺失或重复时重新分配()
    {
        var (valid, problems) = LibraryValidator.Validate(
        [
            E("same", "第一条", "echo 1"),
            E("same", "第二条", "echo 2"),
            new CommandEntry { Id = "  ", Title = "第三条", Command = "echo 3", Category = "Git", Group = "基础操作" },
        ]);

        Assert.Equal(3, valid.Count);
        Assert.Equal(3, valid.Select(v => v.Id).Distinct().Count());
        Assert.Contains(problems, p => p.Contains("Id 重复"));
    }

    [Fact]
    public void 空集合不算错误()
    {
        var (valid, problems) = LibraryValidator.Validate(null);

        Assert.Empty(valid);
        Assert.Empty(problems);
    }
}
