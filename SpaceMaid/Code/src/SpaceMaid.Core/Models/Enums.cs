namespace SpaceMaid.Core.Models;

/// <summary>
/// 清理分级。判据是"删掉之后会发生什么"，不是"它在哪个目录"（需求 2.1）。
/// </summary>
public enum CleanCategory
{
    /// <summary>L1 一键直清：系统/软件自己生成的纯缓存，删掉自动重建。</summary>
    L1OneClick,

    /// <summary>L2 推荐清理：用户数据或可再下载资源，多数默认勾选。</summary>
    L2Recommended,

    /// <summary>L3 谨慎清理：可能影响系统功能或不可逆，一律默认不勾。</summary>
    L3Cautious,

    /// <summary>回收站：语义特殊，单列一档，默认不勾。</summary>
    RecycleBin
}

/// <summary>风险等级，驱动界面视觉（安全 / 注意 / 危险）与红色风险文案（需求 5.4）。</summary>
public enum ItemRisk
{
    Safe,
    Caution,
    Dangerous
}

/// <summary>清理项的落地动作类型。</summary>
public enum CleanActionKind
{
    /// <summary>移入隔离区（绝大多数项）。</summary>
    Quarantine,

    /// <summary>powercfg /h off：关闭休眠并释放 hiberfil.sys（需授权闸门，需求 3.8）。</summary>
    HibernateOff,

    /// <summary>DISM 官方组件清理（禁止 /ResetBase）。</summary>
    DismComponentCleanup,

    /// <summary>只展示不执行（如 pagefile.sys）。</summary>
    InformationalOnly
}

/// <summary>目标枚举方式。</summary>
public enum TargetKind
{
    /// <summary>目录下的文件（不递归）。</summary>
    DirectoryContents,

    /// <summary>目录树（递归逐文件，不整目录删除）。</summary>
    DirectoryTree,

    /// <summary>按通配符匹配的文件。</summary>
    FileGlob,

    /// <summary>固定路径的单个文件。</summary>
    FixedFile,

    /// <summary>回收站（按 SID 子目录成对处理 $I/$R，默认仅 C 盘）。</summary>
    RecycleBin
}
