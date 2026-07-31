using DllTool.Infrastructure.Files;
using DllTool.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DllTool.Infrastructure;

/// <summary>
/// 基础设施层依赖注入注册。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>注册文件系统、备份目录解析、持久化文件日志等基础设施服务。</summary>
    public static IServiceCollection AddDllToolInfrastructure(this IServiceCollection services, string logDirectory, string applicationName)
    {
        services.AddSingleton<DllTool.Core.Abstractions.IFileSystemService, FileSystemService>();

        var loggerProvider = new FileLoggerProvider(Path.Combine(logDirectory, applicationName));
        services.AddSingleton(loggerProvider);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(loggerProvider);
        });

        return services;
    }
}
