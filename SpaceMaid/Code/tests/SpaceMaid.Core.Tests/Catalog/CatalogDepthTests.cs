using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Scanning;
using SpaceMaid.Core.Tests.Abstractions;
using SpaceMaid.Core.Tests.Scanning;

namespace SpaceMaid.Core.Tests.Catalog;

/// <summary>
/// 回归测试：缓存型条目必须**递归**枚举，否则内容都在子目录里的缓存（NuGet 包目录、
/// Chromium 的 Cache\Cache_Data、Gradle caches…）会一个文件都扫不到——这曾经是真实缺陷。
/// </summary>
public class CatalogDepthTests
{
    private static readonly string[] CacheItemIds =
    {
        "l1.user-temp", "l1.windows-temp", "l1.wu-download", "l1.wer", "l1.cbs-logs",
        "l1.delivery-optimization", "l1.packages-temp", "l1.app-logs-vscode", "l1.app-logs-jetbrains",
        "l2.browser-cache", "l2.dev-caches", "l2.driver-downloader", "l2.prefetch", "l2.crash-dumps",
        "l3.chat-cache"
    };

    private static readonly string[] ShallowItemIds =
    {
        "l2.downloads-installers", "l3.large-files", "l3.duplicate-files", "l3.orphan-app-dirs"
    };

    [Fact]
    public void Cache_items_should_be_recursive()
    {
        foreach (var id in CacheItemIds)
        {
            var item = CleanItemCatalog.ById(id);
            Assert.NotNull(item);

            foreach (var rule in item!.Targets.Where(r => r.Kind == TargetKind.DirectoryContents))
            {
                Assert.True(rule.Recurse, $"{id} 的目录内容规则 {rule.Path} 必须递归（缓存内容在子目录里）");
            }
        }
    }

    [Fact]
    public void User_data_items_should_stay_shallow()
    {
        foreach (var id in ShallowItemIds)
        {
            var item = CleanItemCatalog.ById(id);
            Assert.NotNull(item);

            foreach (var rule in item!.Targets)
            {
                Assert.False(rule.Recurse, $"{id} 不应该递归（需求 4.1-6：不递归删除用户目录）");
            }
        }
    }

    [Fact]
    public async Task Nested_cache_files_should_be_found_end_to_end()
    {
        using var root = new TempRoot();
        // NuGet 包缓存：内容全在 <包名>\<版本>\... 里
        root.WriteFile(@"UserProfile\.nuget\packages\newtonsoft.json\13.0.3\lib\net6.0\a.dll", "x");
        // Chromium 的 Cache\Cache_Data 同样是子目录
        root.WriteFile(@"LocalAppData\Google\Chrome\User Data\Default\Cache\Cache_Data\data_1", "y");

        var environment = new CatalogProbe(root.Path);
        var item = CleanItemCatalog.ById("l2.dev-caches")!;
        var engine = new ScanEngine(new WindowsFileSystem(), environment, new ScanFakeVolumeProbe(), new FakeClock(DateTimeOffset.Now));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.True(entry.Available);
        Assert.Contains(entry.Files, f => f.Path.EndsWith("a.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Downloads_item_should_not_descend_into_subfolders()
    {
        using var root = new TempRoot();
        var installer = root.WriteFile(@"UserProfile\Downloads\setup.exe", "installer");
        var nested = root.WriteFile(@"UserProfile\Downloads\extracted\inner.exe", "inner");   // 子目录里的不该被扫到

        // 该项有 30 天年龄门槛，测试文件要"变旧"才能进入候选
        var old = DateTime.UtcNow.AddDays(-60);
        File.SetLastWriteTimeUtc(installer, old);
        File.SetLastWriteTimeUtc(nested, old);

        var environment = new CatalogProbe(root.Path);
        var item = CleanItemCatalog.ById("l2.downloads-installers")!;
        var engine = new ScanEngine(new WindowsFileSystem(), environment, new ScanFakeVolumeProbe(), new FakeClock(DateTimeOffset.Now));

        var report = await engine.ScanAsync(new ScanRequest(new[] { item }, false), null, CancellationToken.None);

        var entry = report.Entries.Single();
        Assert.Single(entry.Files);
        Assert.EndsWith(@"Downloads\setup.exe", entry.Files[0].Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把条目里的环境变量都指到临时目录，用来在真实文件名结构上验证扫描行为。</summary>
    private sealed class CatalogProbe : IEnvironmentProbe
    {
        private readonly string _root;

        public CatalogProbe(string root) => _root = root;

        public bool IsElevated => true;

        public string SystemDrive => "C:";

        public string ExpandVariables(string raw) => raw
            .Replace("%LOCALAPPDATA%", Path.Combine(_root, "LocalAppData"), StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", Path.Combine(_root, "Roaming"), StringComparison.OrdinalIgnoreCase)
            .Replace("%USERPROFILE%", Path.Combine(_root, "UserProfile"), StringComparison.OrdinalIgnoreCase)
            .Replace("%TEMP%", Path.Combine(_root, "Temp"), StringComparison.OrdinalIgnoreCase);
    }
}
