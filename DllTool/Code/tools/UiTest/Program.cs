using System.IO;
using System.Windows;
using System.Windows.Threading;
using DllTool.App.Services;
using DllTool.App.ViewModels;
using DllTool.Core;
using DllTool.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// 在 STA 线程上运行 Dispatcher 消息循环，测试逻辑在其中调度执行。
var thread = new Thread(() =>
{
    var app = new Application();
    _ = Dispatcher.CurrentDispatcher.BeginInvoke(async () => await RunAsync());
    Dispatcher.Run();
    app.Shutdown();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

async Task RunAsync()
{
    int passed = 0, failed = 0;
    void Assert(bool cond, string name)
    {
        if (cond) { passed++; Console.WriteLine($"  OK {name}"); }
        else { failed++; Console.WriteLine($"  FAIL {name}"); }
    }

    var services = new ServiceCollection();
    services.AddDllToolCore();
    services.AddDllToolInfrastructure(@"D:\logs", "DllToolUiTest");
    services.AddSingleton<IOverwriteConfirmation, FakeConfirmation>();
    services.AddSingleton<DllTool.App.Services.AppSettings>();
    services.AddSingleton<MainViewModel>();
    var provider = services.BuildServiceProvider();

    var vm = provider.GetRequiredService<MainViewModel>();

    // 准备演示环境
    var baseDir = Path.Combine(Path.GetTempPath(), "DllToolUiTest_" + Guid.NewGuid().ToString("N")[..6]);
    string target = Path.Combine(baseDir, "target");
    string source = Path.Combine(baseDir, "source");
    string backup = Path.Combine(baseDir, "backup");
    Directory.CreateDirectory(Path.Combine(target, "sub"));
    Directory.CreateDirectory(Path.Combine(source, "sub"));
    Directory.CreateDirectory(backup);
    File.WriteAllText(Path.Combine(target, "core.dll"), "old-core");
    File.WriteAllText(Path.Combine(target, "sub", "plugin.dll"), "old-plugin");
    File.WriteAllText(Path.Combine(source, "core.dll"), "new-core");
    File.WriteAllText(Path.Combine(source, "sub", "plugin.dll"), "new-plugin");

    // ===== 模式A 完整流程 =====
    vm.IsModeA = true;
    vm.NewDllDirectory = source;
    vm.TargetDirectory = target;
    vm.BackupRootA = backup;

    vm.ExecuteCommand.Execute(null);
    while (vm.IsBusy) await Task.Delay(50);

    Assert(vm.ResultRows.Count >= 4, "模式A结果表格包含 4 行（2备份+2覆盖）");
    Assert(File.Exists(Path.Combine(backup, "core.dll")), "备份目录有 core.dll");
    Assert(File.Exists(Path.Combine(backup, "sub", "plugin.dll")), "备份目录保留 sub\\plugin.dll 结构");
    Assert(File.ReadAllText(Path.Combine(target, "core.dll")) == "new-core", "目标 core.dll 已被新版覆盖");
    Assert(vm.BackupDirectoryDisplayA is not null, "模式A界面展示备份目录");
    Assert(vm.ManifestPathDisplayA is not null, "模式A界面展示备份清单路径");

    // ===== 模式B 完整流程 =====
    string manifest = Path.Combine(baseDir, "list.txt");
    File.WriteAllText(manifest, "core.dll\nsub\\plugin.dll\nmissing.dll\n");
    var backup2 = Path.Combine(baseDir, "backup2");
    Directory.CreateDirectory(backup2);
    try
    {
        Console.WriteLine(">>> 切换到模式B");
        vm.IsModeA = false;
        Console.WriteLine(">>> 已切换，设置模式B 独立状态");
        vm.ManifestFile = manifest;
        vm.TargetDirectory = target; // 模式B 有独立的目标目录
        vm.BackupRootB = backup2;
        Console.WriteLine(">>> 执行模式B");
        vm.ExecuteCommand.Execute(null);
        while (vm.IsBusy) await Task.Delay(50);
        Console.WriteLine(">>> 模式B 执行完成");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"!!! 模式B 异常: {ex}");
        throw;
    }

    Assert(vm.ResultRows.Count == 3, "模式B结果表格 3 行（2成功+1跳过）");
    Assert(File.Exists(Path.Combine(backup2, "core.dll")), "模式B备份 core.dll");
    Assert(vm.BackupDirectoryDisplayB is not null, "模式B界面展示备份目录");

    // ===== 模式独立状态验证 =====
    // 记录模式A/B各自状态，切换后应互不干扰
    string aBackupDir = vm.BackupDirectoryDisplayA!;
    string bBackupDir = vm.BackupDirectoryDisplayB!;
    // 模式A 执行完应有的行数：2备份 + 2覆盖 = 4
    int aRowCount = 4;

    // 切回模式A：应恢复模式A的路径与结果
    vm.IsModeA = true;
    Console.WriteLine($">>> 切回模式A: aRowCount={aRowCount}, 当前行数={vm.ResultRows.Count}");
    Assert(vm.ActiveBackupRoot == backup, "切回模式A后 ActiveBackupRoot 显示模式A备份根");
    Assert(vm.NewDllDirectory == source, "切回模式A恢复 新DLL文件夹");
    Assert(vm.BackupRootA == backup, "切回模式A恢复 备份根");
    Assert(vm.BackupDirectoryDisplayA == aBackupDir, "切回模式A恢复 备份目录显示");
    Assert(vm.ResultRows.Count == aRowCount, "切回模式A恢复 结果表格行数");

    // 切回模式B：应恢复模式B的路径与结果
    vm.IsModeA = false;
    Assert(vm.ActiveBackupRoot == backup2, "切回模式B后 ActiveBackupRoot 显示模式B备份根");
    Assert(vm.ManifestFile == manifest, "切回模式B恢复 清单文件");
    Assert(vm.BackupRootB == backup2, "切回模式B恢复 备份根");
    Assert(vm.BackupDirectoryDisplayB == bBackupDir, "切回模式B恢复 备份目录显示");
    Assert(vm.ResultRows.Count == 3, "切回模式B恢复 结果表格 3 行");

    Console.WriteLine();
    Console.WriteLine($"UI测试结果：{passed} 通过，{failed} 失败");
    Dispatcher.CurrentDispatcher.InvokeShutdown();
    Environment.Exit(failed == 0 ? 0 : 1);
}

/// <summary>假覆盖确认器：始终确认覆盖。</summary>
sealed class FakeConfirmation : IOverwriteConfirmation
{
    public bool Confirm() => true;
}
