using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>搜索关键词高亮切分：命中片段要准确，且不能把相邻命中切碎。</summary>
public class HighlightTextTests
{
    private static string Render(string text, string? query)
        => string.Concat(HighlightText.Split(text, query).Select(s => s.IsMatch ? $"[{s.Text}]" : s.Text));

    [Fact]
    public void 单个关键词命中标题片段()
    {
        var segments = HighlightText.Split("查看端口占用", "端口");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new HighlightSegment("查看", false), segments[0]);
        Assert.Equal(new HighlightSegment("端口", true), segments[1]);
        Assert.Equal(new HighlightSegment("占用", false), segments[2]);
    }

    [Fact]
    public void 忽略大小写()
    {
        Assert.Equal("[Compress]-Archive", Render("Compress-Archive", "compress"));
        Assert.Equal("Get-[Net]TCPConnection", Render("Get-NetTCPConnection", "net"));
    }

    [Fact]
    public void 同一个词出现多次都高亮()
    {
        Assert.Equal("[ab]-[ab]-[ab]", Render("ab-ab-ab", "ab"));
    }

    [Fact]
    public void 相邻与重叠的命中合并成一段()
    {
        // "ab bc" 两个词在 "abc" 上命中区间重叠，应合并
        Assert.Equal("[abc]", Render("abc", "ab bc"));
        // 相邻（首尾相接）也要合并，否则会出现两个紧邻的高亮块
        Assert.Equal("[abc]d", Render("abcd", "ab bc"));
    }

    [Fact]
    public void 多关键词按空白拆分并分别高亮()
    {
        Assert.Equal("[git] switch -c [main]", Render("git switch -c main", "git  main"));
    }

    [Fact]
    public void 没有命中时返回整段普通文本()
    {
        var segments = Assert.Single(HighlightText.Split("git status -sb", "zzz"));
        Assert.False(segments.IsMatch);
        Assert.Equal("git status -sb", segments.Text);
    }

    [Fact]
    public void 关键词为空时返回整段普通文本()
    {
        Assert.Equal("git status -sb", Assert.Single(HighlightText.Split("git status -sb", "")).Text);
        Assert.Equal("git status -sb", Assert.Single(HighlightText.Split("git status -sb", "   ")).Text);
    }

    [Fact]
    public void 原文为空时返回空结果()
    {
        Assert.Empty(HighlightText.Split("", "git"));
        Assert.Empty(HighlightText.Split(null, "git"));
    }

    [Fact]
    public void 关键词比原文长时不报错()
    {
        Assert.Equal("git", Assert.Single(HighlightText.Split("git", "git status")).Text);
    }
}
