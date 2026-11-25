using System.IO;
using QuietRemind.Services;
using Xunit;

namespace QuietRemind.Tests;

public class LogServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "QuietRemindTests", Guid.NewGuid().ToString("N"));

    public LogServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响断言结果
        }
    }

    [Fact]
    public void 多线程并发写入_每行完整且无BOM()
    {
        using (var log = new LogService(_dir))
        {
            Parallel.For(0, 100, i => log.Info($"并发-{i}"));
        }

        var file = Assert.Single(Directory.GetFiles(_dir, "*.log"));
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var lines = File.ReadAllText(file)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[INFO\] 并发-\d+$", line));
    }

    [Fact]
    public void 早期日志与实例日志写入同一文件_行不互相截断()
    {
        using (var log = new LogService(_dir))
        {
            log.Info("实例行-前");
            LogService.AppendEarly(_dir, "早期实例退出原因");
            log.Info("实例行-后");
        }

        var file = Assert.Single(Directory.GetFiles(_dir, "*.log"));
        var lines = File.ReadAllText(file)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, l => l.EndsWith("实例行-前"));
        Assert.Contains(lines, l => l.EndsWith("[INFO] 早期实例退出原因"));
        Assert.Contains(lines, l => l.EndsWith("实例行-后"));
    }
}
