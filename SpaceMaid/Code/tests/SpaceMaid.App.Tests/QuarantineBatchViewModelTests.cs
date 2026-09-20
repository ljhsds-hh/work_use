using SpaceMaid.App.ViewModels;
using SpaceMaid.Core.Models;

namespace SpaceMaid.App.Tests;

/// <summary>
/// 一键还原（需求 2.1 / 3.4-6）的界面层回归：批次视图模型必须把"还原了什么、跳过了什么、失败在哪"
/// 如实说出来，并且冲突（原位置已有同名文件）绝不能被算成"已还原"。
/// </summary>
public class QuarantineBatchViewModelTests
{
    private static BatchInfo Batch(bool expired = false) => new(
        "20260920-143012-118",
        new DateTimeOffset(2026, 9, 20, 14, 30, 12, TimeSpan.FromHours(8)),
        new DateTimeOffset(2026, 9, 27, 14, 30, 12, TimeSpan.FromHours(8)),
        1024L * 1024 * 512,
        3,
        expired);

    [Fact]
    public void Summary_should_show_batch_id_files_size_and_expiry()
    {
        var viewModel = new QuarantineBatchViewModel(Batch(), _ => new RestoreResult(0, 0, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>()));

        Assert.Contains("20260920-143012-118", viewModel.SummaryText);
        Assert.Contains("3 个文件", viewModel.SummaryText);
        Assert.Contains("512 MB", viewModel.SummaryText);
        Assert.Contains("到期 2026-09-27", viewModel.SummaryText);
    }

    [Fact]
    public void Restore_should_call_bridge_with_its_own_batch_id_and_report_result()
    {
        string? requested = null;
        var viewModel = new QuarantineBatchViewModel(Batch(), batchId =>
        {
            requested = batchId;
            return new RestoreResult(3, 1536, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>());
        });

        var result = viewModel.Restore();

        Assert.Equal("20260920-143012-118", requested);
        Assert.Equal(3, result.RestoredCount);
        Assert.Contains("已还原 3 个文件", viewModel.StatusText);
    }

    [Fact]
    public void Restore_should_report_conflicts_and_failures_verbatim()
    {
        var viewModel = new QuarantineBatchViewModel(Batch(), _ => new RestoreResult(
            1,
            512,
            new[] { new RestoreConflict(@"C:\Temp\a.tmp", "原位置已存在同名文件（未覆盖）") },
            new[] { new RestoreFailure(@"D:\gone\b.tmp", "原卷不可用（盘符不存在）") }));

        viewModel.Restore();

        Assert.Contains("已还原 1 个文件", viewModel.StatusText);
        Assert.Contains("跳过 1 个", viewModel.StatusText);
        Assert.Contains("失败 1 个", viewModel.StatusText);
        Assert.Contains("原卷不可用", viewModel.StatusText);
    }

    [Fact]
    public void Expired_batch_should_be_flagged()
    {
        var expired = new QuarantineBatchViewModel(Batch(expired: true), _ => new RestoreResult(0, 0, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>()));
        var fresh = new QuarantineBatchViewModel(Batch(), _ => new RestoreResult(0, 0, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>()));

        Assert.True(expired.IsExpired);
        Assert.Contains("保留期已过", expired.ExpiredText);
        Assert.False(fresh.IsExpired);
        Assert.Equal(string.Empty, fresh.ExpiredText);
    }

    [Fact]
    public void Restore_should_notify_after_completion()
    {
        RestoreResult? notified = null;
        var viewModel = new QuarantineBatchViewModel(
            Batch(),
            _ => new RestoreResult(2, 1024, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>()),
            result => notified = result);

        viewModel.Restore();

        Assert.NotNull(notified);
        Assert.Equal(2, notified!.RestoredCount);
    }
}
