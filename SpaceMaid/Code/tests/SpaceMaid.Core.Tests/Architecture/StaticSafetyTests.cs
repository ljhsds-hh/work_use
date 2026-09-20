using System.Text.RegularExpressions;

namespace SpaceMaid.Core.Tests.Architecture;

/// <summary>
/// 静态安全检索测试（需求第 7 章验收第 9/10 条：**可静态检索验证**）。
///
/// 为什么用"读源码文本"的方式做测试：需求里有几条不变量是**关于"不存在"的**
/// （代码里不存在 VSS 删除路径、不存在重置基线开关、不存在绕过确认的开关），
/// 这类要求无法用行为测试证明，只能在源码层面立规矩，让任何后来者一违反就红。
/// </summary>
public class StaticSafetyTests
{
    /// <summary>全项目唯一允许出现删除 API 的位置（含各自理由）。</summary>
    private static readonly Dictionary<string, string> DeleteAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"Platform\WindowsFileSystem.cs"] = "IFileSystem 的唯一实现：所有删除动作最终都收口到这里",
        [@"Quarantine\QuarantineStore.cs"] = "隔离区释放/清空/批次目录删除的唯一合法调用点（不变量 I-1）",
        [@"Quarantine\QuarantineService.cs"] = "还原失败时回滚刚刚写出的副本（不删用户的任何文件）",
        [@"Logging\LogHousekeeping.cs"] = "日志滚动清理（只删匹配 spacemaid-yyyyMMdd.log 的自家日志）"
    };

    private static string CoreSourceRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "SpaceMaid.Core");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到 Core 源码目录（测试必须在仓库内运行）");
        }
    }

    private static IReadOnlyList<(string File, string Text)> ReadCoreSources() =>
        Directory
            .EnumerateFiles(CoreSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path.GetRelativePath(CoreSourceRoot, path), File.ReadAllText(path)))
            .ToList();

    [Fact]
    public void Core_should_not_reference_vss_or_shadow_copy_deletion()
    {
        // 需求 2.6 / 4.2 / 第 7 章第 10 条：系统还原点与卷影副本永不进入清理清单，
        // 代码库中不得存在任何删除它们的调用路径。
        foreach (var (file, text) in ReadCoreSources())
        {
            Assert.DoesNotContain("vssadmin", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DeleteShadow", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DeleteSnapshot", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Core_should_not_use_component_store_reset_base()
    {
        // 需求 2.4 / 设计文档 7.2：只有 /StartComponentCleanup，绝不重置基线（那会让所有更新无法卸载）。
        // 这里刻意断言**裸字串**而不带斜杠：否则文案里写一句"不提供 ResetBase"就能让用例"看着通过"，
        // 却拦不住真的把开关写进命令参数。
        foreach (var (file, text) in ReadCoreSources())
        {
            Assert.DoesNotContain("ResetBase", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Delete_calls_should_only_appear_in_allowlisted_files()
    {
        var offenders = new List<string>();

        foreach (var (file, text) in ReadCoreSources())
        {
            if (DeleteAllowlist.ContainsKey(file))
            {
                continue;
            }

            if (text.Contains("File.Delete(", StringComparison.Ordinal)
                || text.Contains("Directory.Delete(", StringComparison.Ordinal)
                || text.Contains(".TryDeleteFile(", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        Assert.True(offenders.Count == 0,
            "以下文件出现了删除调用，但不在白名单内：" + string.Join("；", offenders)
            + "。允许的位置：" + string.Join("、", DeleteAllowlist.Keys));
    }

    [Fact]
    public void Core_should_not_depend_on_wpf()
    {
        // 设计决策 D-1：内核零 WPF 依赖，否则无法脱离 UI 单测
        foreach (var (file, text) in ReadCoreSources())
        {
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PresentationCore", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Core_should_not_offer_bypass_switches()
    {
        // 需求 4.3-5 / 不变量 I-6：不存在"高级模式/强制清理/跳过确认"这类开关
        var forbidden = new[] { "SkipConfirm", "NoConfirm", "ForceClean", "ForceDelete", "BypassSafety", "IgnoreDenylist", "AllowDenied" };

        foreach (var (file, text) in ReadCoreSources())
        {
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Hibernate_file_must_never_be_a_deletion_target()
    {
        // 需求 3.8 / 4.2：hiberfil.sys 只能通过 powercfg /h off 释放，绝不作为删除目标。
        // 用"目标路径 + 禁止清单"两条可验证事实来断言，而不是简单禁止这个字符串出现
        // （清单的后果说明与注释里提到它是必要的）。
        var hibernate = SpaceMaid.Core.Catalog.CleanItemCatalog.ById("l3.hibernate");
        Assert.NotNull(hibernate);
        Assert.Equal(SpaceMaid.Core.Models.CleanActionKind.HibernateOff, hibernate!.ActionKind);
        Assert.Empty(hibernate.Targets);

        foreach (var item in SpaceMaid.Core.Catalog.CleanItemCatalog.All)
        {
            foreach (var rule in item.Targets)
            {
                Assert.DoesNotContain("hiberfil", rule.Path, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hiberfil", rule.SubPathPattern ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        var hibernatePath = Path.Combine(SpaceMaid.Core.Safety.Denylist.SystemVolumeRoot, "hiberfil.sys");
        Assert.True(SpaceMaid.Core.Safety.Denylist.IsDenied(hibernatePath));
    }

    [Fact]
    public void Catalog_should_not_hardcode_drive_letters()
    {
        // 所有清理项路径必须用 %VAR% 模板，否则换个系统盘就全错
        var catalogFile = Path.Combine(CoreSourceRoot, "Catalog", "CleanItemCatalog.cs");
        var text = File.ReadAllText(catalogFile);

        var matches = Regex.Matches(text, "\"[A-Za-z]:\\\\");
        Assert.True(matches.Count == 0, $"Catalog 中出现硬编码盘符：{matches.Count} 处");
    }

    [Fact]
    public void Catalog_should_have_no_violations()
    {
        // 把清单自检也纳入"静态安全"这一层：任何新增条目都必须过这 10 条规则
        var violations = SpaceMaid.Core.Catalog.CatalogValidator.Validate(SpaceMaid.Core.Catalog.CleanItemCatalog.All);

        Assert.Empty(violations);
    }

    [Fact]
    public void Source_root_should_be_discovered()
    {
        Assert.True(Directory.Exists(CoreSourceRoot));
        Assert.NotEmpty(ReadCoreSources());
    }

    /// <summary>界面工程源码目录（与内核同级）。</summary>
    private static string AppSourceRoot =>
        Path.Combine(Directory.GetParent(CoreSourceRoot)!.FullName, "SpaceMaid.App");

    private static IReadOnlyList<(string File, string Text)> ReadAppSources() =>
        Directory
            .EnumerateFiles(AppSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path.GetRelativePath(AppSourceRoot, path), File.ReadAllText(path)))
            .ToList();

    [Fact]
    public void App_should_not_contain_delete_calls()
    {
        // 界面层永远不删任何东西：它只能请求内核去"隔离"。
        // 这条守卫的意义是：以后有人在 ViewModel 里图省事写一句 File.Delete，会立刻变红。
        var offenders = new List<string>();

        foreach (var (file, text) in ReadAppSources())
        {
            if (text.Contains("File.Delete(", StringComparison.Ordinal)
                || text.Contains("Directory.Delete(", StringComparison.Ordinal)
                || text.Contains(".TryDeleteFile(", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        Assert.True(offenders.Count == 0,
            "界面层出现了删除调用（界面只能请求内核隔离，不能自己删）：" + string.Join("；", offenders));
    }

    [Fact]
    public void App_should_not_offer_bypass_switches_or_reset_base()
    {
        var forbidden = new[]
        {
            "SkipConfirm", "NoConfirm", "ForceClean", "ForceDelete", "BypassSafety", "IgnoreDenylist", "AllowDenied", "ResetBase"
        };

        foreach (var (file, text) in ReadAppSources())
        {
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void App_should_not_own_a_command_runner()
    {
        // 界面**必然**会在文案里提到 powercfg（需求 3.8 就要求把"将关闭休眠、可逆"讲清楚），
        // 所以这里不能查命令名字，而要查"界面有没有自己执行命令的能力"：
        // 只要界面拿不到 ICommandRunner / ProcessCommandRunner，它就无从绕过内核的专项动作去跑命令。
        foreach (var (file, text) in ReadAppSources())
        {
            Assert.DoesNotContain("ProcessCommandRunner", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ICommandRunner", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 覆盖式移动/复制（<c>overwrite: true</c>）事实上有"删除原内容"的效果，因此也要进白名单审计。
    /// 三个合法位置：文件系统实现（原子替换）、隔离区账本写盘、设置写盘——都只覆盖自己刚写的临时文件。
    /// </summary>
    [Fact]
    public void Overwriting_moves_and_copies_should_only_appear_in_allowlisted_files()
    {
        var allowlist = new[]
        {
            @"Platform\WindowsFileSystem.cs",
            @"Quarantine\QuarantineMapStore.cs",
            @"Settings\SettingsStore.cs"
        };

        var offenders = new List<string>();

        foreach (var (file, text) in ReadCoreSources())
        {
            if (allowlist.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (text.Contains("overwrite: true", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        Assert.True(offenders.Count == 0,
            "以下文件出现了覆盖式移动/复制，但不在白名单内：" + string.Join("；", offenders));
    }

    /// <summary>
    /// 结构化检查（比查字符串更难绕过）：内核**公开方法**的命名里不允许出现
    /// Force / Skip / Bypass / Ignore 这类暗示"可以绕过"的词。
    /// 改个名字就能骗过文本检索，但骗不过反射。
    /// </summary>
    [Fact]
    public void Public_api_should_not_smell_like_a_bypass()
    {
        var suspicious = new[] { "force", "bypass", "ignore", "override" };
        var offenders = new List<string>();

        var coreAssembly = typeof(SpaceMaid.Core.Marker).Assembly;
        foreach (var type in coreAssembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public
                                                     | System.Reflection.BindingFlags.Instance
                                                     | System.Reflection.BindingFlags.Static
                                                     | System.Reflection.BindingFlags.DeclaredOnly))
            {
                var name = method.Name.ToLowerInvariant();
                if (suspicious.Any(token => name.Contains(token, StringComparison.Ordinal)))
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "内核公开方法名里出现了可疑词（疑似绕过开关）：" + string.Join("；", offenders));
    }

    [Fact]
    public void App_source_root_should_be_discovered()
    {
        Assert.True(Directory.Exists(AppSourceRoot), $"找不到界面源码目录：{AppSourceRoot}");
        Assert.NotEmpty(ReadAppSources());
    }
}
