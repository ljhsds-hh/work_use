using SpaceMaid.Core.Models;

namespace SpaceMaid.Core.Tests.Reporting;

internal sealed class ReportingFakeClock : SpaceMaid.Core.Abstractions.IClock
{
    public ReportingFakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}

/// <summary>构造清理计划/扫描报告的测试夹具。</summary>
internal static class PlanFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 9, 20, 14, 30, 0, TimeSpan.FromHours(8));

    public static ScanFile File(
        string path,
        long size,
        int daysAgo = 1,
        CleanActionKind action = CleanActionKind.Quarantine) =>
        new(path, size, Now.AddDays(-daysAgo), action);

    public static ScanEntry Entry(CleanItemDefinition item, params ScanFile[] files) =>
        new(item, files.Sum(f => f.Size), files.Length, 0, files, true, null);

    public static CleanItemDefinition Definition(
        string id,
        CleanCategory category,
        CleanActionKind action = CleanActionKind.Quarantine,
        ItemRisk risk = ItemRisk.Safe,
        string actionNote = "",
        string sideEffect = "",
        string restoreHint = "") => new()
        {
            Id = id,
            Category = category,
            DisplayName = $"测试项 {id}",
            Risk = risk,
            ActionKind = action,
            Targets = new[] { TargetRule.Contents(@"%TEMP%") },
            ActionNote = actionNote,
            SideEffect = sideEffect,
            RestoreHint = restoreHint
        };

    public static CleanPlanItem PlanItem(
        CleanItemDefinition definition,
        bool userChecked,
        params ScanFile[] files) => new(
        definition.Id,
        definition.Category,
        definition.DisplayName,
        "移入隔离区",
        files,
        files.Sum(f => f.Size),
        DefaultChecked: definition.Category == CleanCategory.L1OneClick,
        UserChecked: userChecked)
    {
        ActionNote = definition.ActionNote,
        RestoreHint = definition.RestoreHint,
        SideEffect = definition.SideEffect,
        Risk = definition.Risk,
        ActionKind = definition.ActionKind
    };

    public static CleanPlan Plan(IReadOnlyList<CleanPlanItem> items, IReadOnlyList<ScanEntry> entries, bool sameVolume = true) =>
        new(
            "20260920-143000-000",
            Now,
            new ScanReport(entries, new VolumeSnapshot(@"C:\", 500L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024), Now),
            items,
            items.Sum(i => i.TotalBytes),
            sameVolume);
}
