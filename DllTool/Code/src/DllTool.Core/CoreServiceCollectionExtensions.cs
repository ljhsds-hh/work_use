using DllTool.Core.Backup;
using DllTool.Core.Services;
using DllTool.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace DllTool.Core;

/// <summary>
/// 核心层依赖注入注册。
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>注册备份目录解析、两模式执行器、流程校验等核心服务。</summary>
    public static IServiceCollection AddDllToolCore(this IServiceCollection services)
    {
        services.AddSingleton<BackupDirectoryResolver>();
        services.AddSingleton<FlowValidator>();
        services.AddSingleton<ModeAExecutor>();
        services.AddSingleton<ModeBExecutor>();
        return services;
    }
}
