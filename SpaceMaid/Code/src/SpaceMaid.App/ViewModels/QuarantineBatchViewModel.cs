using SpaceMaid.App.Helpers;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Reporting;

namespace SpaceMaid.App.ViewModels;

/// <summary>
/// 隔离区里的一个批次（需求 2.1 / 3.4-6：保留期内可一键还原）。
///
/// 为什么单独做成一个视图模型：还原是**逐批次**的动作，界面需要"这一批是什么、多大、什么时候到期、
/// 现在还在不在"都摆在一行里，用户才敢点那个按钮。
/// </summary>
public sealed class QuarantineBatchViewModel : ViewModelBase
{
    private readonly Func<string, RestoreResult> _restore;
    private string _statusText = string.Empty;

    public QuarantineBatchViewModel(
        BatchInfo batch,
        Func<string, RestoreResult> restore,
        Action<RestoreResult>? onRestored = null)
    {
        Batch = batch;
        _restore = restore;
        OnRestored = onRestored;
        RestoreCommand = new RelayCommand(() => Restore());
    }

    public BatchInfo Batch { get; }

    public Action<RestoreResult>? OnRestored { get; }

    /// <summary>
    /// 可选的二次确认。返回 false 时**一个文件都不动**，状态文案写"已取消"。
    ///
    /// 为什么做成可选：设置窗口在调用前已经自己确认过一次（它要按批次拼更长的文案），
    /// 主窗的隔离区页则需要自己弹确认——两处共用同一个视图模型，但确认的归属方不同。
    /// </summary>
    public Func<bool>? Confirm { get; init; }

    public string BatchId => Batch.BatchId;

    public RelayCommand RestoreCommand { get; }

    /// <summary>一行说明：批次号 · 文件数 · 体积 · 到期时间。</summary>
    public string SummaryText =>
        $"{Batch.BatchId} · {Batch.EntryCount} 个文件 · {VolumeTextFormatter.FormatBytes(Batch.TotalBytes)}"
        + $" · 到期 {Batch.ExpiresAt:yyyy-MM-dd HH:mm}";

    /// <summary>已到期批次要被明确标出来：它们的保留期已经过了，还原不再是"万无一失"。</summary>
    public bool IsExpired => Batch.Expired;

    public string ExpiredText => IsExpired ? "已到期（保留期已过，可能随时被释放）" : string.Empty;

    /// <summary>最近一次还原结果（读给用户听的那句话）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>执行还原并返回结果；界面在调用前必须先完成二次确认。</summary>
    public RestoreResult Restore()
    {
        if (Confirm is not null && !Confirm())
        {
            StatusText = "已取消，隔离区未做任何改动。";
            return new RestoreResult(0, 0, Array.Empty<RestoreConflict>(), Array.Empty<RestoreFailure>());
        }

        var result = _restore(BatchId);

        var parts = new List<string> { $"已还原 {result.RestoredCount} 个文件（{VolumeTextFormatter.FormatBytes(result.RestoredBytes)}）" };
        if (result.Conflicts.Count > 0)
        {
            parts.Add($"跳过 {result.Conflicts.Count} 个：原位置已有同名文件，未覆盖");
        }

        if (result.Failures.Count > 0)
        {
            parts.Add($"失败 {result.Failures.Count} 个：{result.Failures[0].Reason}");
        }

        StatusText = string.Join("；", parts);
        OnRestored?.Invoke(result);
        return result;
    }
}
