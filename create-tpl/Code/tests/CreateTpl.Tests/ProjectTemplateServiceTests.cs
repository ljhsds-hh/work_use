using System.IO;
using CreateTpl.Models;
using CreateTpl.Services;
using Xunit;

namespace CreateTpl.Tests;

/// <summary>ProjectTemplateService 模板创建引擎单元测试（对应需求 3.2 / 3.3 / 3.4 / 3.6）。</summary>
public class ProjectTemplateServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectTemplateService _service = new();

    public ProjectTemplateServiceTests()
    {
        // 每个测试使用独立的临时根目录，互不干扰
        _root = Path.Combine(Path.GetTempPath(), "create-tpl-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结论
        }
    }

    // ─────────────── 单工程创建：固定结构 ───────────────

    [Fact]
    public async Task CreateProject_CreatesFixedStructure_Completely()
    {
        var result = await _service.CreateProjectAsync(_root, "MyProj");

        Assert.Equal(CreateStatus.Success, result.Status);
        var dir = Path.Combine(_root, "MyProj");

        Assert.True(Directory.Exists(Path.Combine(dir, "Code")));
        Assert.True(Directory.Exists(Path.Combine(dir, "Docs")));

        // README.md / Docs/需求.md 完全空白（0 字节，需求 3.3）
        Assert.Empty(await File.ReadAllTextAsync(Path.Combine(dir, "README.md")));
        Assert.Empty(await File.ReadAllTextAsync(Path.Combine(dir, "Docs", "需求.md")));

        // CLAUDE.md 预置固定文案（需求 3.3）
        Assert.Equal(ProjectTemplateService.ClaudeMemoText,
            await File.ReadAllTextAsync(Path.Combine(dir, "CLAUDE.md")));

        // 成功结果携带完整生成路径（需求 3.5）
        Assert.Equal(Path.GetFullPath(dir), result.Message);
    }

    [Fact]
    public async Task CreateProject_RootAutoCreated_WhenMissing()
    {
        var deepRoot = Path.Combine(_root, "level1", "level2");

        var result = await _service.CreateProjectAsync(deepRoot, "Proj");

        Assert.Equal(CreateStatus.Success, result.Status);
        Assert.True(Directory.Exists(Path.Combine(deepRoot, "Proj", "Docs")));
    }

    // ─────────────── 防重复机制（需求 3.4） ───────────────

    [Fact]
    public async Task CreateProject_ExistingProject_SkipsAndNeverOverwrites()
    {
        await _service.CreateProjectAsync(_root, "MyProj");

        // 手动修改已有文件内容，验证重复创建绝不覆盖
        var claudePath = Path.Combine(_root, "MyProj", "CLAUDE.md");
        await File.WriteAllTextAsync(claudePath, "用户已修改的内容");

        var result = await _service.CreateProjectAsync(_root, "MyProj");

        Assert.Equal(CreateStatus.Skipped, result.Status);
        Assert.Equal("当前工程已存在，无需重复创建", result.Message);
        Assert.Equal("用户已修改的内容", await File.ReadAllTextAsync(claudePath));
    }

    // ─────────────── 批量：单点失败隔离（需求 3.6） ───────────────

    [Fact]
    public async Task CreateBatch_ExistingProject_DoesNotBlockSubsequent()
    {
        await _service.CreateProjectAsync(_root, "Exists");

        var results = await _service.CreateBatchAsync(_root, new[] { "Exists", "NewOne" });

        Assert.Equal(2, results.Count);
        Assert.Equal(CreateStatus.Skipped, results[0].Status);
        Assert.Equal(CreateStatus.Success, results[1].Status);
        Assert.True(Directory.Exists(Path.Combine(_root, "NewOne", "Code")));
    }

    [Fact]
    public async Task CreateBatch_FailedProject_DoesNotBlockSubsequent()
    {
        // 在根目录放置与工程同名的"文件"，使该工程目录创建必然失败（模拟磁盘/权限类异常）
        await File.WriteAllTextAsync(Path.Combine(_root, "BadProj"), "占位文件");

        var results = await _service.CreateBatchAsync(_root, new[] { "BadProj", "GoodProj" });

        Assert.Equal(2, results.Count);
        Assert.Equal(CreateStatus.Failed, results[0].Status);
        Assert.Contains("创建失败", results[0].Message);
        Assert.Equal(CreateStatus.Success, results[1].Status);
        Assert.True(Directory.Exists(Path.Combine(_root, "GoodProj", "Docs")));
    }

    [Fact]
    public async Task CreateBatch_RootCreationFailure_AllProjectsFailWithReason()
    {
        // 根路径被同名文件占用 → 根目录创建失败 → 全部工程失败且原因明确
        var fileRoot = Path.Combine(_root, "rootAsFile");
        await File.WriteAllTextAsync(fileRoot, "占位");

        var results = await _service.CreateBatchAsync(fileRoot, new[] { "A", "B" });

        Assert.All(results, r => Assert.Equal(CreateStatus.Failed, r.Status));
        Assert.All(results, r => Assert.Contains("根目录创建失败", r.Message));
    }

    [Fact]
    public async Task CreateBatch_FailedProjectIsRolledBack_NoPartialDirectoryLeft()
    {
        // 目录同名占位文件场景：不应残留"BadProj"残缺目录
        await File.WriteAllTextAsync(Path.Combine(_root, "BadProj"), "占位文件");

        await _service.CreateBatchAsync(_root, new[] { "BadProj" });

        // BadProj 是文件（回滚不应误删用户文件）
        Assert.True(File.Exists(Path.Combine(_root, "BadProj")));
    }

    [Fact]
    public async Task CreateBatch_ReportsEachResultViaProgress()
    {
        var reported = new List<ProjectResult>();
        var progress = new Progress<ProjectResult>(reported.Add);

        var results = await _service.CreateBatchAsync(_root, new[] { "P1", "P2", "P3" }, progress);

        // Progress 异步回调，等待封送完成
        await Task.Delay(100);
        Assert.Equal(results.Count, reported.Count);
    }

    // ─────────────── 目录树预览（需求 3.5） ───────────────

    [Fact]
    public void BuildTreePreview_ContainsAllTemplateEntries()
    {
        var preview = _service.BuildTreePreview("D:\\Projects", "MyProj");

        Assert.Contains("MyProj", preview);
        Assert.Contains("CLAUDE.md", preview);
        Assert.Contains("README.md", preview);
        Assert.Contains("Code/", preview);
        Assert.Contains("Docs/", preview);
        Assert.Contains("需求.md", preview);
    }
}
