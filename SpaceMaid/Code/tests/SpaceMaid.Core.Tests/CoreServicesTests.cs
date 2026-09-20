using SpaceMaid.Core.Abstractions;
using SpaceMaid.Core.Models;
using SpaceMaid.Core.Settings;
using SpaceMaid.Core.Tests.Execution;
using SpaceMaid.Core.Tests.Scanning;

namespace SpaceMaid.Core.Tests;

public class CoreServicesTests
{
    private static CoreServices CreateServices(IFileSystem? fileSystem = null)
    {
        var settings = new AppSettings
        {
            QuarantineBasePath = @"D:\Quarantine",
            LogDirectory = @"D:\logs\SpaceMaid",
            ReportDirectory = @"D:\logs\SpaceMaid\清单"
        };

        return CoreServices.Create(
            settings,
            fileSystem: fileSystem ?? new ScriptedFileSystem(),
            clock: new FakeClock(DateTimeOffset.Now),
            volumes: new SpaceMaid.Core.Tests.Quarantine.MappedVolumeProbe(),
            environment: new ExecutionFakeEnvironment(),
            commandRunner: new FakeCommandRunner(),
            log: SpaceMaid.Core.Logging.SilentLogSink.Instance);
    }

    private static CleanItemDefinition Definition(
        string id,
        CleanCategory category,
        bool defaultChecked,
        CleanActionKind action = CleanActionKind.Quarantine) => new()
        {
            Id = id,
            Category = category,
            DisplayName = $"项 {id}",
            Risk = category == CleanCategory.L1OneClick ? ItemRisk.Safe : ItemRisk.Caution,
            ActionKind = action,
            Targets = new[] { TargetRule.Contents(@"C:\Temp") },
            AutoRegenerated = defaultChecked,
            DefaultChecked = defaultChecked,
            ActionNote = category == CleanCategory.L1OneClick ? string.Empty : "需你确认",
            RestoreHint = category == CleanCategory.L1OneClick ? string.Empty : "从隔离区还原"
        };

    private static ScanReport Report(params (CleanItemDefinition Definition, ScanFile[] Files)[] entries)
    {
        var list = entries.Select(e => new ScanEntry(
            e.Definition,
            e.Files.Sum(f => f.Size),
            e.Files.Length,
            0,
            e.Files,
            true,
            null)).ToList();

        return new ScanReport(
            list,
            new VolumeSnapshot(@"C:\", 500L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024),
            DateTimeOffset.Now);
    }

    [Fact]
    public void Should_wire_every_component()
    {
        var services = CreateServices();

        Assert.NotNull(services.Scanner);
        Assert.NotNull(services.Executor);
        Assert.NotNull(services.Quarantine);
        Assert.NotNull(services.Manifest);
        Assert.NotNull(services.Reviewer);
        Assert.NotNull(services.SafetyGate);
        Assert.Equal(2, services.SpecialHandlers.Count);
        Assert.NotEmpty(services.Catalog);
        Assert.EndsWith(@"SpaceMaid\Quarantine", services.QuarantineRoot);
    }

    [Fact]
    public void Should_build_plan_with_conservative_defaults()
    {
        var services = CreateServices();
        var l1 = Definition("l1.user-temp", CleanCategory.L1OneClick, defaultChecked: true);
        var l3 = Definition("l3.chat-cache", CleanCategory.L3Cautious, defaultChecked: false);

        var plan = services.BuildPlan(Report(
            (l1, new[] { new ScanFile(@"C:\Temp\a.tmp", 100, DateTimeOffset.Now, CleanActionKind.Quarantine) }),
            (l3, new[] { new ScanFile(@"C:\Temp\b.dat", 200, DateTimeOffset.Now, CleanActionKind.Quarantine) })));

        Assert.Equal(2, plan.Items.Count);
        Assert.True(plan.Items.Single(i => i.ItemId == "l1.user-temp").UserChecked);
        Assert.False(plan.Items.Single(i => i.ItemId == "l3.chat-cache").UserChecked);
        Assert.Equal(100, plan.QuarantineRequiredBytes);   // 只算勾选的
    }

    [Fact]
    public void Should_apply_user_check_override_without_remembering_anything()
    {
        var services = CreateServices();
        var l3 = Definition("l3.chat-cache", CleanCategory.L3Cautious, defaultChecked: false);

        var report = Report((l3, new[] { new ScanFile(@"C:\Temp\b.dat", 200, DateTimeOffset.Now, CleanActionKind.Quarantine) }));

        var checkedPlan = services.BuildPlan(report, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "l3.chat-cache" });
        Assert.True(checkedPlan.Items.Single().UserChecked);
        Assert.Equal(200, checkedPlan.QuarantineRequiredBytes);

        // 再建一次（不传勾选）必须回到默认不勾——不记忆上次勾选（需求 5.4-2）
        var freshPlan = services.BuildPlan(report);
        Assert.False(freshPlan.Items.Single().UserChecked);
        Assert.Equal(0, freshPlan.QuarantineRequiredBytes);
    }

    [Fact]
    public void Should_include_command_items_without_files()
    {
        var services = CreateServices();
        var hibernate = Definition("l3.hibernate", CleanCategory.L3Cautious, defaultChecked: false, action: CleanActionKind.HibernateOff);

        var plan = services.BuildPlan(Report((hibernate, Array.Empty<ScanFile>())));

        var item = Assert.Single(plan.Items);
        Assert.Equal(CleanActionKind.HibernateOff, item.ActionKind);
        Assert.False(item.UserChecked);
        Assert.Equal(0, plan.QuarantineRequiredBytes);
    }

    [Fact]
    public void Should_execution_options_default_to_unauthorized_hibernate()
    {
        var services = CreateServices();

        var options = services.BuildExecutionOptions(authorizeHibernate: false);

        Assert.False(options.AuthorizeHibernate);
        Assert.Equal(7, options.RetentionDays);
        Assert.EndsWith(@"SpaceMaid\Quarantine", options.QuarantineRoot);
    }

    [Fact]
    public void Prepare_should_report_unusable_quarantine_instead_of_throwing()
    {
        var services = CreateServices();

        var preparation = services.Prepare();

        // ScriptedFileSystem 说任何目录都不存在 → 校验必然拒绝，但不能抛异常
        Assert.False(preparation.QuarantineUsable);
        Assert.False(string.IsNullOrWhiteSpace(preparation.QuarantineMessage));
        Assert.NotNull(preparation.Recovery);
        Assert.NotNull(preparation.Release);
    }
}
