using SpaceMaid.Core.Catalog;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Platform;
using SpaceMaid.Core.Safety;

namespace SpaceMaid.Core.Tests.Safety;

/// <summary>
/// 把**真实清单条目**喂进安全闸门（对抗式评审指出的覆盖缺口）：
/// 此前所有 SafetyGate 用例都用手工造的假条目与假路径，于是"回收站条目在闸门处被 100% 拒绝"
/// 这种功能性阻断在测试里完全看不出来。
/// </summary>
public class RealCatalogAuthorizationTests
{
    private static SafetyGate CreateGate() =>
        new(new WindowsFileSystem(), new WindowsEnvironmentProbe());

    [Fact]
    public void Recycle_bin_item_should_authorize_real_recycle_bin_paths()
    {
        var item = CleanItemCatalog.ById("rb.recycle-bin");
        Assert.NotNull(item);

        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var candidate = Path.Combine(systemDrive + Path.DirectorySeparatorChar, "$Recycle.Bin", "S-1-5-21-1111", "$RABCDEF.txt");

        var decision = CreateGate().Authorize(candidate, item!);

        Assert.True(decision.IsAllowed, $"回收站条目必须能授权回收站内的文件，实际：{decision.Verdict} / {decision.Reason}");
    }

    [Fact]
    public void Recycle_bin_item_should_still_reject_paths_outside_the_drive()
    {
        var item = CleanItemCatalog.ById("rb.recycle-bin")!;
        var decision = CreateGate().Authorize(@"D:\$Recycle.Bin\S-1-5-21-1\$R1.txt", item);

        Assert.Equal(SafetyVerdict.OutsideAllowlist, decision.Verdict);
    }

    [Fact]
    public void Temporary_folder_items_should_authorize_their_own_files()
    {
        // 用真实条目的真实目标根 + 一个典型候选文件，验证"温存目录本身可写"这条最常用的路径是通的
        foreach (var id in new[] { "l1.user-temp", "l1.windows-temp" })
        {
            var item = CleanItemCatalog.ById(id);
            Assert.NotNull(item);

            var rule = item!.Targets.First(r => r.Kind != TargetKind.RecycleBin);
            var root = new WindowsEnvironmentProbe().ExpandVariables(rule.Path);
            var candidate = Path.Combine(root, "sample.tmp");

            var decision = CreateGate().Authorize(candidate, item);
            Assert.True(decision.IsAllowed, $"{id} 应放行自己的临时文件，实际：{decision.Verdict} / {decision.Reason}");
        }
    }

    [Fact]
    public void Informational_item_should_never_be_authorizable_for_quarantine()
    {
        // pagefile.sys 是"只展示、不可清理"，它的目标必须命中禁止清单
        var item = CleanItemCatalog.ById("l3.pagefile");
        Assert.NotNull(item);

        var rule = item!.Targets.Single();
        var path = new WindowsEnvironmentProbe().ExpandVariables(rule.Path);
        var decision = CreateGate().Authorize(path, item);

        Assert.Equal(SafetyVerdict.DeniedByDenylist, decision.Verdict);
    }
}
