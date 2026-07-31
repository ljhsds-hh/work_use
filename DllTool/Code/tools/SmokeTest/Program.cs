using DllTool.Core.Abstractions;
using DllTool.Core.Backup;
using DllTool.Core.Services;
using DllTool.Core.Validation;
using DllTool.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;

var fileSystem = new FileSystemService(NullLogger<FileSystemService>.Instance);
var backupResolver = new BackupDirectoryResolver(fileSystem, NullLogger<BackupDirectoryResolver>.Instance);

int passed = 0, failed = 0;

void Assert(bool condition, string name, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  ✓ {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  ✗ {name} {detail}");
    }
}

string Temp(string name)
{
    string dir = Path.Combine(Path.GetTempPath(), "DllToolSmoke_" + Guid.NewGuid().ToString("N")[..8], name);
    Directory.CreateDirectory(dir);
    return dir;
}

void Touch(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, $"content-{Guid.NewGuid()}");
}

// ============ 场景1：模式A 完整流程（备份 + 覆盖） ============
Console.WriteLine("场景1：模式A 完整流程");
string scene1Target = Temp("scene1");
string scene1New = Temp("scene1New");
string scene1BackupRoot = Temp("scene1Backup");

Touch(Path.Combine(scene1Target, "core.dll"));          // 两端重合
Touch(Path.Combine(scene1Target, "sub", "plugin.dll")); // 两端重合（子目录）
Touch(Path.Combine(scene1Target, "only_old.dll"));      // 仅目标存在

Touch(Path.Combine(scene1New, "core.dll"));
Touch(Path.Combine(scene1New, "sub", "plugin.dll"));
Touch(Path.Combine(scene1New, "only_new.dll"));         // 仅新端存在

var modeA = new ModeAExecutor(fileSystem, backupResolver, NullLogger<ModeAExecutor>.Instance);

var backupResult = modeA.Backup(scene1New, scene1Target, scene1BackupRoot);
Assert(backupResult.MatchedRelativePaths.Count == 2, "重合清单应为2个（core.dll + sub\\plugin.dll）");
Assert(File.Exists(Path.Combine(backupResult.BackupDirectory!, "core.dll")), "备份目录中存在 core.dll");
Assert(File.Exists(Path.Combine(backupResult.BackupDirectory!, "sub", "plugin.dll")), "备份目录保留子目录结构 sub\\plugin.dll");
Assert(File.Exists(Path.Combine(scene1Target, "core.dll")), "源文件保留原位（复制非移动）");
Assert(File.Exists(backupResult.ManifestPath!), "生成备份清单文件");
string manifestContent = File.ReadAllText(backupResult.ManifestPath!);
Assert(manifestContent.Contains("core.dll"), "清单包含 core.dll");
Assert(manifestContent.Contains("sub\\plugin.dll"), "清单包含 sub\\plugin.dll（相对路径）");
Assert(!manifestContent.Contains("only_old.dll") && !manifestContent.Contains("only_new.dll"), "清单不含单方文件");

var overwriteResult = modeA.Overwrite(scene1New, scene1Target, backupResult);
Assert(overwriteResult.OverwriteExecuted, "覆盖已执行");
Assert(overwriteResult.Entries.Count(e => e.Action == "覆盖" && e.Status == DllTool.Core.Models.OperationStatus.Success) == 2, "覆盖成功2个");
Assert(File.ReadAllText(Path.Combine(scene1Target, "core.dll")).StartsWith("content-"), "目标目录 core.dll 已被新内容覆盖");
Assert(File.Exists(Path.Combine(scene1Target, "only_old.dll")), "目标独有文件保留");
Assert(!File.Exists(Path.Combine(scene1Target, "only_new.dll")), "新端独有文件未写入目标目录");

// ============ 场景2：模式A 空重合 ============
Console.WriteLine("场景2：模式A 空重合");
string scene2Target = Temp("scene2");
string scene2New = Temp("scene2New");
Touch(Path.Combine(scene2Target, "a.dll"));
Touch(Path.Combine(scene2New, "b.dll"));
var emptyResult = modeA.Backup(scene2New, scene2Target, Temp("scene2Backup"));
Assert(emptyResult.MatchedRelativePaths.Count == 0, "空重合时不生成备份目录");
Assert(emptyResult.BackupDirectory is null, "空重合时 BackupDirectory 为 null");

// ============ 场景3：模式B（清单文件） ============
Console.WriteLine("场景3：模式B");
string scene3Target = Temp("scene3");
string scene3BackupRoot = Temp("scene3Backup");
Touch(Path.Combine(scene3Target, "alpha.dll"));
Touch(Path.Combine(scene3Target, "sub", "beta.dll"));
string manifest3 = Path.Combine(Temp("scene3Manifest"), "list.txt");
File.WriteAllText(manifest3, "alpha.dll\nsub\\beta.dll\nmissing.dll\n#注释行\n\n");

var modeB = new ModeBExecutor(fileSystem, backupResolver, NullLogger<ModeBExecutor>.Instance);
var bResult = modeB.Execute(manifest3, scene3Target, scene3BackupRoot);

Assert(File.Exists(Path.Combine(bResult.BackupDirectory!, "alpha.dll")), "备份 alpha.dll");
Assert(File.Exists(Path.Combine(bResult.BackupDirectory!, "sub", "beta.dll")), "备份 sub\\beta.dll（保留结构）");
Assert(!File.Exists(Path.Combine(bResult.BackupDirectory!, "missing.dll")), "未匹配条目未备份");
Assert(bResult.SkippedCount == 1, "未匹配条目记入 Skipped");
string bManifest = File.ReadAllText(bResult.ManifestPath!);
Assert(bManifest.Contains("alpha.dll") && bManifest.Contains("sub\\beta.dll"), "清单含成功备份条目");
Assert(!bManifest.Contains("missing.dll"), "清单不含未匹配条目");
Assert(bResult.OverwriteExecuted == false, "模式B不执行覆盖");

// ============ 场景4：非空备份根目录自动创建子文件夹 ============
Console.WriteLine("场景4：非空备份根目录");
string scene4Target = Temp("scene4");
string scene4New = Temp("scene4New");
string scene4BackupRoot = Temp("scene4Backup");
Touch(Path.Combine(scene4BackupRoot, "历史文件.txt"));
Touch(Path.Combine(scene4Target, "x.dll"));
Touch(Path.Combine(scene4New, "x.dll"));
var r4 = modeA.Backup(scene4New, scene4Target, scene4BackupRoot);
Assert(Path.GetFileName(r4.BackupDirectory!) != "历史文件.txt", "非空根目录下创建了专属子文件夹");
Assert(File.Exists(Path.Combine(r4.BackupDirectory!, "x.dll")), "备份文件在子文件夹中");

// ============ 场景5：校验器 ============
Console.WriteLine("场景5：校验器");
var validator = new FlowValidator(fileSystem);
string scene5Dir = Temp("scene5");
Assert(validator.ValidateModeA(scene5Dir, scene5Dir) is not null, "模式A源目录=目标目录被拒绝");
Assert(validator.ValidateModeA(null!, scene5Dir) is not null, "模式A空源目录被拒绝");
string scene5New2 = Temp("scene5New");
Assert(validator.ValidateModeA(scene5New2, scene5Dir) is null, "模式A合法输入通过");
string scene5Manifest = Path.Combine(Temp("scene5M"), "m.txt");
File.WriteAllText(scene5Manifest, "a.dll\n");
Assert(validator.ValidateModeB(scene5Manifest, scene5Dir) is null, "模式B合法输入通过");
Assert(validator.ValidateModeB("D:\\不存在的文件.txt", scene5Dir) is not null, "模式B不存在的清单文件被拒绝");

// ============ 场景6：大小写敏感匹配 ============
Console.WriteLine("场景6：大小写敏感匹配");
string scene6Target = Temp("scene6");
string scene6New = Temp("scene6New");
string scene6Backup = Temp("scene6Backup");
Touch(Path.Combine(scene6Target, "Case.dll"));
Touch(Path.Combine(scene6New, "case.dll"));
var r6 = modeA.Backup(scene6New, scene6Target, scene6Backup);
Assert(r6.MatchedRelativePaths.Count == 0, "case.dll 与 Case.dll 因大小写不同不匹配");

// ============ 场景7：源目录包含目标目录被拒绝 ============
Console.WriteLine("场景7：包含关系校验");
string scene7Target = Temp("scene7");
string scene7Inside = Path.Combine(scene7Target, "backup");
Directory.CreateDirectory(scene7Inside);
Assert(validator.ValidateModeA(scene7Target, scene7Inside) is not null, "源目录包含目标目录被拒绝");

// ============ 场景8：备份根目录校验 ============
Console.WriteLine("场景8：备份根目录校验");
string scene8Target = Temp("scene8");
string scene8New = Temp("scene8New");
string scene8BackupInside = Path.Combine(scene8Target, "bak");
Directory.CreateDirectory(scene8BackupInside);
Assert(validator.ValidateBackupRoot(scene8BackupInside, scene8Target, scene8New) is not null, "备份根位于目标目录内被拒绝");
Assert(validator.ValidateBackupRoot(scene8Target, scene8Target, scene8New) is not null, "备份根=目标目录被拒绝");
Assert(validator.ValidateBackupRoot(scene8New, scene8Target, scene8New) is not null, "备份根=新DLL目录被拒绝");
Assert(validator.ValidateBackupRoot(Temp("scene8Safe"), scene8Target, scene8New) is null, "独立备份根通过");
Assert(validator.ValidateBackupRoot("", scene8Target, scene8New) is not null, "空备份根被拒绝");

Console.WriteLine();
Console.WriteLine($"结果：{passed} 通过，{failed} 失败");
return failed == 0 ? 0 : 1;
