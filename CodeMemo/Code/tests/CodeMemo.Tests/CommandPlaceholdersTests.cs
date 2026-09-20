using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>占位符解析与填充（命令里 &lt;xxx&gt; 参数的成对约定由此落地成功能）。</summary>
public class CommandPlaceholdersTests
{
    [Fact]
    public void 提取占位符_去重且保持出现顺序()
    {
        var names = CommandPlaceholders.Extract("git switch -c <新分支名> origin/<基准分支> <新分支名>");

        Assert.Equal(["新分支名", "基准分支"], names);
    }

    [Fact]
    public void 无占位符时返回空()
    {
        Assert.Empty(CommandPlaceholders.Extract("git status -sb"));
        Assert.Empty(CommandPlaceholders.Extract(""));
        Assert.Empty(CommandPlaceholders.Extract(null));
        Assert.False(CommandPlaceholders.HasPlaceholder("git status -sb"));
    }

    [Fact]
    public void 填充_已填参数被替换_留空的保持原样()
    {
        var filled = CommandPlaceholders.Fill(
            "git switch -c <新分支名> origin/<基准分支>",
            new Dictionary<string, string> { ["新分支名"] = " feature/login ", ["基准分支"] = "   " });

        Assert.Equal("git switch -c feature/login origin/<基准分支>", filled);
    }

    [Fact]
    public void 填充_同名占位符全部替换()
    {
        var filled = CommandPlaceholders.Fill(
            "echo <值> && echo <值>",
            new Dictionary<string, string> { ["值"] = "hi" });

        Assert.Equal("echo hi && echo hi", filled);
    }

    [Fact]
    public void 填充_没有值时返回原文()
    {
        const string command = "Get-ChildItem <目录路径> -Recurse";
        Assert.Equal(command, CommandPlaceholders.Fill(command, null));
        Assert.Equal(command, CommandPlaceholders.Fill(command, new Dictionary<string, string>()));
    }
}
