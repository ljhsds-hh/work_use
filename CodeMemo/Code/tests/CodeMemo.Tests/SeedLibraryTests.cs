using System.Text.RegularExpressions;
using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>内置命令库种子数据合法性：保证开箱即用的数据本身不出错。</summary>
public class SeedLibraryTests
{
    private static IReadOnlyList<CommandEntry> Seed() => SeedLibrary.Load().Commands;

    [Fact]
    public void Seed_资源存在且可解析()
    {
        // 资源缺失时 Load 会抛 InvalidOperationException，此处不抛即资源存在且解析成功
        var data = SeedLibrary.Load();
        Assert.NotEmpty(data.Commands);
    }

    [Fact]
    public void Seed_条目数量达标()
    {
        Assert.InRange(Seed().Count(c => c.Category == "PowerShell"), 30, int.MaxValue);
        Assert.InRange(Seed().Count(c => c.Category == "Git"), 40, int.MaxValue);
    }

    [Fact]
    public void Seed_必填字段齐全且分类分组合法()
    {
        foreach (var entry in Seed())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id), $"Id 为空：{entry.Title}");
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"Title 为空：{entry.Id}");
            Assert.False(string.IsNullOrWhiteSpace(entry.Command), $"Command 为空：{entry.Title}");
            Assert.False(string.IsNullOrWhiteSpace(entry.Note), $"Note 为空：{entry.Title}");
            Assert.True(Catalog.IsValid(entry.Category, entry.Group),
                $"分类/分组不合法：{entry.Title} → {entry.Category} / {entry.Group}");
        }
    }

    [Fact]
    public void Seed_Id无重复()
    {
        var ids = Seed().Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Seed_尖括号占位符成对()
    {
        // 可替换参数统一 <xxx> 标注：校验去掉所有 <xxx> 后不残留孤立的 < 或 >
        var placeholder = new Regex("<[^<>\\n]+>");
        foreach (var entry in Seed())
        {
            var remains = placeholder.Replace(entry.Command, "");
            Assert.True(!remains.Contains('<') && !remains.Contains('>'),
                $"占位符不成对：{entry.Title} → {entry.Command}");
        }
    }

    [Fact]
    public void Seed_标题无重复()
    {
        var titles = Seed().Select(c => c.Title).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }
}
