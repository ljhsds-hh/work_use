using SpaceMaid.App.ViewModels;
using SpaceMaid.Core.Models;

namespace SpaceMaid.App.Tests;

/// <summary>
/// 对抗式评审 F-14 的回归：页面文件（信息项）**不能有勾选框、也不能被计入"已勾选"体积**。
/// 需求 2.4 明确它是"仅展示，不可清理"。
/// </summary>
public class CleanItemViewModelTests
{
    private static CleanItemViewModel CreateInformationalItem()
    {
        var item = new CleanPlanItem(
            "l3.pagefile",
            CleanCategory.L3Cautious,
            "页面文件",
            "仅展示，不执行",
            new[] { new ScanFile(@"C:\pagefile.sys", 8L * 1024 * 1024 * 1024, DateTimeOffset.Now, CleanActionKind.InformationalOnly) },
            8L * 1024 * 1024 * 1024,
            DefaultChecked: false,
            UserChecked: false)
        {
            ActionKind = CleanActionKind.InformationalOnly,
            ActionNote = "仅展示，不可清理",
            Risk = ItemRisk.Caution
        };

        return new CleanItemViewModel(item);
    }

    [Fact]
    public void Informational_item_should_not_show_a_checkbox()
    {
        var viewModel = CreateInformationalItem();

        Assert.True(viewModel.IsInformationalOnly);
        Assert.False(viewModel.ShowCheckBox);
    }

    [Fact]
    public void Informational_item_should_refuse_to_be_checked()
    {
        var viewModel = CreateInformationalItem();

        viewModel.IsChecked = true;

        Assert.False(viewModel.IsChecked, "信息项不允许被勾选，否则会被算进「本次可处理」体积");
    }

    [Fact]
    public void Normal_l3_item_should_still_show_a_checkbox()
    {
        var item = new CleanPlanItem(
            "l3.chat-cache",
            CleanCategory.L3Cautious,
            "聊天缓存",
            "移入隔离区",
            Array.Empty<ScanFile>(),
            0,
            DefaultChecked: false,
            UserChecked: false)
        {
            ActionKind = CleanActionKind.Quarantine,
            Risk = ItemRisk.Dangerous
        };

        var viewModel = new CleanItemViewModel(item);

        Assert.True(viewModel.ShowCheckBox);
        Assert.True(viewModel.IsDangerous);
    }
}
