using System.IO;
using CodeMemo.Models;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>本地 JSON 持久化：原子写、往返一致、损坏隔离、可读中文、备份轮转、版本护栏。</summary>
public class LibraryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"codememo-test-{Guid.NewGuid():N}");

    private string DataFile => Path.Combine(_dir, "commands.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void 首次加载_文件缺失返回空库()
    {
        var store = new LibraryStore(_dir);
        var (data, problems) = store.Load();
        Assert.Empty(data.Commands);
        Assert.Empty(problems);
        Assert.False(store.Exists);
    }

    [Fact]
    public void 保存后往返一致()
    {
        var store = new LibraryStore(_dir);
        var data = new CommandLibraryData
        {
            Commands =
            [
                new CommandEntry
                {
                    Id = "a1", Title = "标题", Command = "git status -sb", Note = "备注",
                    Category = "Git", Group = "基础操作", UseCount = 3,
                    LastUsedAt = new DateTime(2026, 9, 1, 8, 0, 0),
                },
            ],
        };

        store.Save(data);
        Assert.True(store.Exists);

        var (loaded, problems) = store.Load();
        Assert.Empty(problems);
        var entry = Assert.Single(loaded.Commands);
        Assert.Equal("a1", entry.Id);
        Assert.Equal("git status -sb", entry.Command);
        Assert.Equal(3, entry.UseCount);
        Assert.Equal(new DateTime(2026, 9, 1, 8, 0, 0), entry.LastUsedAt);
        Assert.Equal(LibraryStore.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void 文件损坏时按空库加载且不覆盖原文件()
    {
        var store = new LibraryStore(_dir);
        File.WriteAllText(DataFile, "{ 不是合法 json !!!");

        var (data, problems) = store.Load();
        Assert.Empty(data.Commands);
        Assert.NotEmpty(problems);
        // 原损坏文件保留，未被覆盖
        Assert.True(store.Exists);
        Assert.Contains("不是合法 json", File.ReadAllText(DataFile));
    }

    [Fact]
    public void 保存的_JSON_不转义中文与尖括号()
    {
        var store = new LibraryStore(_dir);
        store.Save(new CommandLibraryData
        {
            Commands =
            [
                new CommandEntry
                {
                    Id = "1", Title = "查看端口占用",
                    Command = "Get-NetTCPConnection -LocalPort <端口号>",
                    Note = "排查端口被占用", Category = "PowerShell", Group = "进程与服务",
                },
            ],
        });

        var text = File.ReadAllText(DataFile);
        Assert.Contains("查看端口占用", text);
        Assert.Contains("<端口号>", text);
        Assert.DoesNotContain("\\u", text);   // 不再出现 \uXXXX 转义，数据文件可读可手改
    }

    [Fact]
    public void 导入备份带时间戳且只保留最近几份()
    {
        var store = new LibraryStore(_dir);
        store.Save(new CommandLibraryData());

        string? last = null;
        for (var i = 0; i < LibraryStore.MaxBackups + 3; i++)
        {
            last = store.BackupBeforeImport();
        }

        Assert.NotNull(last);
        Assert.Contains("commands.", Path.GetFileName(last));
        Assert.EndsWith(".bak", last);
        Assert.Equal(LibraryStore.MaxBackups, Directory.GetFiles(_dir, "commands.*.bak").Length);
    }

    [Fact]
    public void 没有数据文件时不生成备份()
    {
        var store = new LibraryStore(_dir);

        Assert.Null(store.BackupBeforeImport());
        Assert.Empty(Directory.GetFiles(_dir, "commands.*.bak"));
    }

    [Fact]
    public void 文件版本高于程序时给出提示且不擅自改写()
    {
        var store = new LibraryStore(_dir);
        File.WriteAllText(DataFile, "{\"SchemaVersion\":99,\"Commands\":[]}");

        var (data, problems) = store.Load();

        Assert.Equal(99, data.SchemaVersion);
        Assert.Contains("v99", Assert.Single(problems));
    }

    [Fact]
    public void 缺少版本号时按当前版本加载并提示()
    {
        var store = new LibraryStore(_dir);
        File.WriteAllText(DataFile, "{\"Commands\":[]}");

        var (data, problems) = store.Load();

        Assert.Equal(LibraryStore.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Single(problems);
    }

    [Fact]
    public void 数据目录属性指向数据文件所在目录()
    {
        var store = new LibraryStore(_dir);

        Assert.Equal(_dir, store.DirectoryPath);
        Assert.Equal(Path.Combine(_dir, "commands.json"), store.FilePath);
    }
}
