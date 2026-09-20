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

    /// <summary>
    /// 不变量 I-6 的**第二个入口**：环境变量。
    ///
    /// 为什么单独立一条：命令行开关能被上面那条用例挡住，但"读环境变量决定要不要跳过安全检查"
    /// 同样是一个绕过入口，而且更隐蔽——它在源码里长得像普通的配置读取。
    /// 这条不是凭空的：修批次分配锁时我自己就临时加过一个 `SPACEMAID_DISABLE_...` 开关做反向验证，
    /// 事后靠人眼确认删干净了。有了这条用例，那种"忘了删"会直接红。
    ///
    /// 只拦"看起来是安全绕过"的名字；读 `SystemDrive` 这类系统变量不受影响。
    /// </summary>
    [Fact]
    public void Core_should_not_read_bypass_style_environment_variables()
    {
        var suspicious = new Regex("disable|skip|bypass|force|ignore|unsafe|nocheck", RegexOptions.IgnoreCase);
        var pattern = new Regex(@"GetEnvironmentVariable\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
        var offenders = new List<string>();

        foreach (var (file, text) in ReadCoreSources())
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (suspicious.IsMatch(match.Groups[1].Value))
                {
                    offenders.Add($"{file}: {match.Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Hibernate_file_must_never_be_a_deletion_target()    {
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
        Assert.NotEmpty(ReadAppXaml());
    }

    /// <summary>
    /// 界面 XAML 里不得出现硬编码颜色（需求 5.3：颜色与字体走 HandyControl 皮肤与设计令牌）。
    ///
    /// 为什么把这条从"人工看一遍"变成自动断言：界面的手写十六进制色值是最容易在评审里溜过去的一类问题——
    /// 看图看不出 `#FF5722` 和 `DangerBrush` 的差别，但深色皮肤一切换就露馅，而且它会绕开主题。
    ///
    /// 判定细节（两处都踩过坑，别改回去）：
    /// ① **先剥掉 XML 注释**：注释里为了讲道理会写出"不许写 Color= / SolidColorBrush"这类字样，
    ///    按原文扫描会把说明文字当成违规；断言的对象应该是真正的标记语言，不是文档。
    /// ② `Color=` 只在**字面量**上违规：`Color="{DynamicResource X}"` 是引用令牌，属于正确写法。
    /// </summary>
    [Fact]
    public void App_xaml_should_not_hardcode_colors()
    {
        var commentPattern = new Regex("<!--.*?-->", RegexOptions.Singleline);
        var hexPattern = new Regex(@"#[0-9A-Fa-f]{3,8}\b");
        var literalColorPattern = new Regex("Color\\s*=\\s*\"(?!\\s*\\{(Dynamic|Static)Resource)[^\"]*\"");
        var offenders = new List<string>();

        foreach (var (file, raw) in ReadAppXaml())
        {
            var text = commentPattern.Replace(raw, string.Empty);

            foreach (Match match in hexPattern.Matches(text))
            {
                offenders.Add($"{file}: {match.Value}");
            }

            foreach (Match match in literalColorPattern.Matches(text))
            {
                offenders.Add($"{file}: {match.Value.Trim()}");
            }

            if (text.Contains("SolidColorBrush", StringComparison.Ordinal))
            {
                offenders.Add($"{file}: 出现 SolidColorBrush 直接定义画刷");
            }
        }

        Assert.True(offenders.Count == 0,
            "界面 XAML 不得硬编码颜色（应使用 HandyControl 皮肤的动态资源或 DesignTokens 令牌）：" + string.Join("；", offenders));
    }

    /// <summary>
    /// 设计令牌字典：**键不得重复**，且界面里引用的**自有令牌（Sm\*）必须真的存在**。
    ///
    /// 为什么值得两条静态用例：这两类错误在编译期都发现不了——
    /// 重复键会让 WPF 在**运行期**抛 "Item has already been added. Key in dictionary: 'X'"
    /// （整个界面直接启动失败）；引用不存在的 `Sm*` 键则会抛"找不到名为 X 的资源"。
    /// 两者都是"改一行 XAML 就炸，但不跑起来完全看不出来"，正适合用文本级断言守着。
    /// （`DynamicResource` 更阴——找不到只是静默变 null，所以这条同时把 Dynamic 引用也查了。）
    /// </summary>
    [Fact]
    public void Design_tokens_should_be_unique_and_referenced_keys_should_exist()
    {
        var (tokenFile, tokenText) = ReadAppXaml().Single(x => x.File.EndsWith("DesignTokens.xaml", StringComparison.OrdinalIgnoreCase));

        var defined = new Regex("x:Key=\"([^\"]+)\"")
            .Matches(tokenText)
            .Select(m => m.Groups[1].Value)
            .ToList();

        var duplicates = defined
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"{tokenFile} 里有重复的资源键（运行期会直接抛异常）：{string.Join('、', duplicates)}");

        var definedSet = new HashSet<string>(defined, StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var (file, raw) in ReadAppXaml())
        {
            var text = new Regex("<!--.*?-->", RegexOptions.Singleline).Replace(raw, string.Empty);

            foreach (Match match in new Regex(@"\{(?:Dynamic|Static)Resource\s+(Sm[A-Za-z0-9]+)\}").Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!definedSet.Contains(key))
                {
                    missing.Add($"{file}: {key}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            $"{tokenFile} 里没有定义这些被界面引用的令牌（运行期会报「找不到资源」，DynamicResource 则静默失效）：{string.Join('、', missing.Distinct())}");
    }

    private static IReadOnlyList<(string File, string Text)> ReadAppXaml() =>
        Directory
            .EnumerateFiles(AppSourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path.GetRelativePath(AppSourceRoot, path), File.ReadAllText(path)))
            .ToList();

    /// <summary>
    /// 启动顺序不变量：**主窗必须先登记为 <c>Application.MainWindow</c>，再跑启动自检**。
    ///
    /// 为什么值得用一条静态用例守着：启动自检里会弹"权限不足"对话框（需求 3.6-2），
    /// 而 WPF 把"第一个显示出来的窗口"记为 <c>Application.MainWindow</c>；配合
    /// <c>ShutdownMode.OnMainWindowClose</c>，那个对话框一关就把整个应用关掉了。
    /// 真机表现是"未提权启动 → 点掉提示 → 程序直接消失"，而且**两只眼睛很难看出来是顺序问题**。
    /// 这条用例读 App.xaml.cs 的源码文本，断言两件事的相对顺序。
    /// </summary>
    [Fact]
    public void App_should_register_main_window_before_running_startup_checks()
    {
        var (file, text) = ReadAppSource("App.xaml.cs");
        var assignIndex = text.IndexOf("MainWindow = window;", StringComparison.Ordinal);
        var initializeIndex = text.IndexOf("viewModel.Initialize()", StringComparison.Ordinal);

        Assert.True(assignIndex >= 0, $"{file} 里找不到 `MainWindow = window;`");
        Assert.True(initializeIndex >= 0, $"{file} 里找不到 `viewModel.Initialize()`");
        Assert.True(assignIndex < initializeIndex,
            $"{file}: `MainWindow = window;` 必须出现在 `viewModel.Initialize()` 之前——"
            + "否则启动自检里弹出的对话框会被 WPF 记成 Application.MainWindow，"
            + "配合 ShutdownMode.OnMainWindowClose 会让应用在用户点掉提示后直接退出。");
    }

    private static (string File, string Text) ReadAppSource(string fileName)
    {
        var path = Directory
            .EnumerateFiles(AppSourceRoot, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                 && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        Assert.False(path is null, $"找不到界面源码 {fileName}");
        return (Path.GetRelativePath(AppSourceRoot, path!), File.ReadAllText(path!));
    }

    /// <summary>
    /// "打开设置"这条链路必须有人订阅。
    ///
    /// 真实踩过的坑：ViewModel 侧的 `RequestOpenSettings` 事件、`OpenSettingsCommand`、
    /// 导航项的 "settings" 分支都在，**但主窗从来没订阅过这个事件**——于是「设置」按钮点下去毫无反应，
    /// 隔离区位置/保留期/清空隔离区/报告日志目录全部进不去，而界面上看不出任何异常。
    /// 这类"接线漏了"的错误只有把界面真跑起来点一下才看得见，所以在这里立一条可静态检查的规矩。
    /// </summary>
    [Fact]
    public void App_should_wire_settings_request_from_viewmodel()
    {
        var (file, text) = ReadAppSource("MainWindow.xaml.cs");

        Assert.Contains("RequestOpenSettings += ShowSettings", text, StringComparison.Ordinal);
        Assert.Contains("RequestOpenSettings -= ShowSettings", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("RequestOpenSettings += ShowSettings", StringComparison.Ordinal)
            < text.IndexOf("RequestOpenSettings -= ShowSettings", StringComparison.Ordinal),
            $"{file}: 订阅必须出现在退订之前（构造函数里订阅、OnClosing 里退订）");
    }
}
