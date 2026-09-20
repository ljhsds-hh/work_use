using System.Reflection;
using SpaceMaid.App.Services;
using SpaceMaid.App.Tests.Fakes;
using SpaceMaid.App.ViewModels;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Quarantine;

namespace SpaceMaid.App.Tests;

/// <summary>
/// 主界面 ViewModel 的行为测试（需求 3.4 / 3.6 / 3.8 / 3.9 / 4.3 / 5.4）。
/// 全部不启动 UI：扫描、执行、对话框、文件夹选择都是假实现。
/// </summary>
public class MainViewModelTests
{
    // ── 需求 5.4-2 / 3.3-5：不记忆勾选状态，每次重新扫描回到默认 ──

    [Fact]
    public async Task Should_reset_check_state_to_defaults_on_rescan()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        var risky = host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache");
        Assert.False(risky.IsChecked);

        risky.IsChecked = true;
        Assert.True(risky.IsChecked);

        await host.ViewModel.RescanAsync();

        var afterRescan = host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache");
        Assert.Equal(afterRescan.DefaultChecked, afterRescan.IsChecked);
        Assert.False(afterRescan.IsChecked);

        // L2 的 Windows.old 是允许默认勾选的少数项，重扫后也必须回到它自己的默认值
        Assert.True(host.ViewModel.Items.Single(i => i.ItemId == "l2.windows-old").IsChecked);
    }

    // ── 需求 3.3-4 / 4.3-1：L3 二次确认，取消则执行器零调用 ──

    [Fact]
    public async Task Should_require_confirmation_for_l3()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        var risky = host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache");
        Assert.True(host.ViewModel.RequiresConfirmation(new[] { risky }));
        Assert.True(risky.RequiresConfirmation);

        risky.IsChecked = true;
        host.Dialogs.NextConfirmResult = false; // 用户点「取消」

        await host.ViewModel.CleanSelectedAsync();

        Assert.Single(host.Dialogs.Confirmations);
        Assert.Equal(0, host.Executor.CallCount);
        Assert.False(host.ViewModel.HasExecutionResult);
        Assert.Contains("已取消", host.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Execute_order_on_confirmed_l3_must_be_confirmation_then_executor()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        var order = new List<string>();
        host.Dialogs.NextConfirmResult = true;
        host.Executor.OnExecute = (_, _) => order.Add("execute");
        host.ViewModel.ConfirmRequested += _ => order.Add("confirm");

        host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache").IsChecked = true;

        await host.ViewModel.CleanSelectedAsync();

        Assert.Equal(new[] { "confirm", "execute" }, order);
        Assert.Equal(1, host.Executor.CallCount);
    }

    // ── 需求 3.8：休眠项必须在确认里写明后果与可逆方式，授权后才传 true ──

    [Fact]
    public async Task Should_ask_hibernate_authorization_and_pass_it_to_executor()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        host.ViewModel.Items.Single(i => i.ItemId == "l3.hibernate").IsChecked = true;
        Assert.True(host.ViewModel.RequiresHibernateAuthorization);

        host.Dialogs.NextConfirmResult = true;
        await host.ViewModel.CleanSelectedAsync();

        var text = Assert.Single(host.Dialogs.Confirmations).Message;
        Assert.Contains("休眠", text);
        Assert.Contains("快速启动", text);
        Assert.Contains("powercfg /h on", text);
        Assert.Contains("不影响睡眠", text);

        Assert.NotNull(host.Executor.LastOptions);
        Assert.True(host.Executor.LastOptions!.AuthorizeHibernate);
    }

    [Fact]
    public async Task Should_not_authorize_hibernate_when_user_cancels()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        host.ViewModel.Items.Single(i => i.ItemId == "l3.hibernate").IsChecked = true;
        host.Dialogs.NextConfirmResult = false;

        await host.ViewModel.CleanSelectedAsync();

        Assert.Equal(0, host.Executor.CallCount);
        Assert.Null(host.Executor.LastOptions);
    }

    // ── 需求 5.4-3/4：风险项红色文案 = 名称（动作性质），并就地给恢复方式 ──

    [Fact]
    public async Task Should_show_danger_text_and_restore_hint_for_risky_items()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        var l3Items = host.ViewModel.Items.Where(i => i.Category == CleanCategory.L3Cautious).ToList();
        Assert.NotEmpty(l3Items);

        foreach (var item in l3Items)
        {
            Assert.True(item.IsDangerous, $"{item.ItemId} 应为风险项");
            Assert.Contains("（", item.DisplayNameWithNote);
            Assert.Contains("）", item.DisplayNameWithNote);
            Assert.Contains(item.ActionNote, item.DisplayNameWithNote);
            Assert.False(string.IsNullOrWhiteSpace(item.RestoreHint), $"{item.ItemId} 必须有恢复方式");
            Assert.True(item.ShowCheckBox, $"{item.ItemId} 需要可勾选");
            Assert.False(item.DefaultChecked, $"{item.ItemId} 必须默认不勾");
        }

        // 三个"必须写清怎么回头"的重点项（需求 5.4-4 点名的场景）
        Assert.Contains("powercfg /h on", host.ViewModel.Items.Single(i => i.ItemId == "l3.hibernate").RestoreHint);
        Assert.Contains("还原", host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache").RestoreHint);
        Assert.Contains("还原", host.ViewModel.Items.Single(i => i.ItemId == "rb.recycle-bin").RestoreHint);

        var hibernate = host.ViewModel.Items.Single(i => i.ItemId == "l3.hibernate");
        Assert.Equal("休眠文件（仅关闭功能，不删文件）", hibernate.DisplayNameWithNote);
        Assert.Equal("休眠文件", hibernate.DisplayName);
        Assert.False(hibernate.IsRestorable);
    }

    [Fact]
    public async Task Should_mark_safe_items_as_not_dangerous()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        var safe = host.ViewModel.Items.Single(i => i.ItemId == "l1.user-temp");
        Assert.False(safe.IsDangerous);
        Assert.False(safe.ShowCheckBox);
        Assert.Equal(safe.DisplayName, safe.DisplayNameWithNote);
    }

    // ── 需求 3.2-6 / 3.4-7（D-6）：同卷口径绝不说"已释放" ──

    [Fact]
    public async Task Should_show_same_volume_notice()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        Assert.Contains("已移入隔离区", host.ViewModel.SameVolumeNotice);
        Assert.DoesNotContain("已释放", host.ViewModel.SameVolumeNotice);

        Assert.Contains("已移入隔离区", host.ViewModel.TotalProcessableText);
        Assert.DoesNotContain("已释放", host.ViewModel.TotalProcessableText);

        Assert.Contains("已移入隔离区", host.ViewModel.ProcessableDetail);
        Assert.DoesNotContain("已释放", host.ViewModel.ProcessableDetail);
    }

    [Fact]
    public async Task Should_use_same_volume_wording_for_execution_result()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        await host.ViewModel.OneClickCleanAsync();

        Assert.Contains("已移入隔离区", host.ViewModel.ResultSummaryText);
        Assert.DoesNotContain("已释放", host.ViewModel.ResultSummaryText);
        Assert.Contains("已移入隔离区", host.ViewModel.ResultDetailText);
        Assert.DoesNotContain("已释放", host.ViewModel.ResultDetailText);
        Assert.True(host.ViewModel.HasExecutionResult);
    }

    // ── 需求 4.3-5（I-6）：不存在任何"绕过确认 / 强制清理"的开关 ──

    [Fact]
    public void Should_not_offer_bypass_switch()
    {
        var forbidden = new[] { "force", "bypass", "skipconfirm", "noconfirm", "silent", "autoclean" };

        foreach (var type in new[] { typeof(MainViewModel), typeof(SettingsViewModel), typeof(CleanItemViewModel) })
        {
            var names = type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m is PropertyInfo or FieldInfo)
                .Select(m => m.Name)
                .ToList();

            Assert.NotEmpty(names);

            foreach (var pattern in forbidden)
            {
                Assert.DoesNotContain(names, n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // ── 需求 3.2 / 3.9-3：一键清理只动 L1，导出与执行不自动衔接 ──

    [Fact]
    public async Task OneClick_clean_should_only_execute_l1_items()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        host.Dialogs.NextConfirmResult = true;
        await host.ViewModel.OneClickCleanAsync();

        Assert.Equal(1, host.Executor.CallCount);
        Assert.NotEmpty(host.Executor.LastPlan!.Checked);
        Assert.All(host.Executor.LastPlan!.Checked, item => Assert.Equal(CleanCategory.L1OneClick, item.Category));
    }

    [Fact]
    public async Task Execute_without_export_must_not_produce_review_report()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        await host.ViewModel.OneClickCleanAsync();

        Assert.True(host.ViewModel.HasExecutionResult);
        Assert.False(host.ViewModel.HasReviewReport);
        Assert.Contains("导出清单", host.ViewModel.ReviewHint);
    }

    [Fact]
    public async Task Clean_selected_must_be_skipped_when_nothing_is_checked()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        foreach (var item in host.ViewModel.Items.Where(i => i.IsChecked))
        {
            item.IsChecked = false;
        }

        Assert.False(host.ViewModel.CanExecuteClean);

        await host.ViewModel.CleanSelectedAsync();

        Assert.Equal(0, host.Executor.CallCount);
        Assert.Empty(host.Dialogs.Confirmations);
    }

    // ── 需求 3.6：未提权时必须阻止进入清理流程 ──

    [Fact]
    public async Task Should_block_clean_when_not_elevated()
    {
        var host = new MainViewModelTestHost(elevated: false);
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        Assert.True(host.ViewModel.IsFlowBlocked);
        Assert.False(host.ViewModel.CanExecuteClean);

        await host.ViewModel.OneClickCleanAsync();

        Assert.Equal(0, host.Executor.CallCount);
        Assert.Contains("管理员", host.ViewModel.BlockReason);
        Assert.Single(host.Dialogs.Warnings);
    }

    // ── 需求 3.1-4：顶部磁盘总览的四个数字 ──

    [Fact]
    public async Task Should_fill_disk_overview_numbers_after_scan()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        Assert.Equal(host.Report.Volume.TotalBytes, host.ViewModel.VolumeTotalBytes);
        Assert.Equal(host.Report.Volume.TotalBytes - host.Report.Volume.FreeBytes, host.ViewModel.VolumeUsedBytes);
        Assert.Equal(host.Report.Volume.FreeBytes, host.ViewModel.VolumeFreeBytes);
        Assert.Equal(host.Report.TotalBytes, host.ViewModel.ProcessableBytes);
        Assert.True(host.ViewModel.UsedPercent > 0);
        Assert.True(host.ViewModel.UsedPercent <= 1);
        Assert.Equal(4, host.ViewModel.Sections.Count);
        Assert.All(host.ViewModel.Sections, s => Assert.True(s.HasItems));
    }

    // ── 需求 3.3-3：全选/反选只作用于当前分级 ──

    [Fact]
    public async Task Toggle_all_must_only_affect_current_category()
    {
        var host = new MainViewModelTestHost();
        await host.ViewModel.InitializeAsync();
        await host.ViewModel.RescanAsync();

        host.ViewModel.SelectedCategory = CleanCategory.L2Recommended;

        for (var round = 0; round < 2; round++)
        {
            host.ViewModel.ToggleAllCurrentCategory();

            var l2 = host.ViewModel.Items
                .Where(i => i.Category == CleanCategory.L2Recommended && i.ShowCheckBox)
                .ToList();

            Assert.NotEmpty(l2);
            Assert.All(l2, item => Assert.Equal(round == 0, item.IsChecked));
        }

        Assert.False(host.ViewModel.Items.Single(i => i.ItemId == "l3.chat-cache").IsChecked);
        Assert.False(host.ViewModel.Items.Single(i => i.ItemId == "rb.recycle-bin").IsChecked);
    }

    // ── 需求 3.4-2：设置页选定即校验，Reject 不允许保存 ──

    [Fact]
    public void Settings_should_reject_save_when_path_is_invalid()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        settings.QuarantineBasePath = @"\\nas\share\quarantine";

        Assert.Equal(QuarantinePathLevel.Reject, settings.ValidationLevel);
        Assert.False(settings.CanSave);
        Assert.False(settings.Save());
        Assert.NotEmpty(settings.ValidationMessage);
    }

    [Fact]
    public void Settings_should_warn_but_allow_save_when_quarantine_stays_on_system_drive()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        settings.QuarantineBasePath = @"C:\SpaceMaidQuarantine";

        Assert.Equal(QuarantinePathLevel.Warn, settings.ValidationLevel);
        Assert.True(settings.CanSave);
        Assert.Contains("不会立刻释放", settings.ValidationMessage);
    }

    [Fact]
    public void Settings_should_show_actual_storage_location_under_selected_base_path()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        settings.QuarantineBasePath = @"D:\SpaceMaidQuarantine";

        Assert.Equal(@"D:\SpaceMaidQuarantine\SpaceMaid\Quarantine", settings.QuarantineStoragePath);
    }

    [Fact]
    public void Settings_should_pick_folder_and_validate_immediately()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        host.FolderPicker.NextResult = @"D:\SpaceMaidQuarantine";

        settings.PickQuarantineFolder();

        Assert.Equal(1, host.FolderPicker.CallCount);
        Assert.Equal(@"D:\SpaceMaidQuarantine", settings.QuarantineBasePath);
        Assert.NotEqual(QuarantinePathLevel.Reject, settings.ValidationLevel);
    }

    [Fact]
    public void Settings_should_clamp_retention_days()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        settings.RetentionDays = 9999;
        Assert.Equal(365, settings.RetentionDays);

        settings.RetentionDays = 0;
        Assert.Equal(1, settings.RetentionDays);
    }

    [Fact]
    public void Settings_should_require_confirmation_before_clearing_quarantine()
    {
        var host = new MainViewModelTestHost();
        var settings = SettingsViewModel.Create(host.Bridge, host.FolderPicker, host.Dialogs, host.Notifications, host.Shell);

        host.Dialogs.NextConfirmResult = false;
        var cancelled = settings.ClearQuarantineNow();

        Assert.Null(cancelled);
        Assert.Contains("不可还原", Assert.Single(host.Dialogs.Confirmations).Message);

        host.Dialogs.NextConfirmResult = true;
        var done = settings.ClearQuarantineNow();

        Assert.NotNull(done);
        Assert.Equal(2, host.Dialogs.Confirmations.Count);
    }
}
