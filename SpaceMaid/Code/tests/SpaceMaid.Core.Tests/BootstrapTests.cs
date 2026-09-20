using SpaceMaid.Core;

namespace SpaceMaid.Core.Tests;

/// <summary>
/// Task 1 骨架冒烟测试：证明测试工程能正确引用内核程序集。
/// </summary>
public class BootstrapTests
{
    [Fact]
    public void Core_assembly_is_referenced()
    {
        Assert.Equal(Marker.AssemblyName, typeof(Marker).Assembly.GetName().Name);
    }

    [Fact]
    public void Core_targets_net8_windows()
    {
        var targetFramework = typeof(Marker).Assembly
            .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), inherit: false)
            .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>()
            .Single()
            .FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v8.0", targetFramework);
    }
}
