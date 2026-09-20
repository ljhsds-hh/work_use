namespace SpaceMaid.App.Tests;

/// <summary>
/// Task 1 骨架冒烟测试：证明测试工程能引用应用工程（界面相关断言在 Task 13 补全）。
/// </summary>
public class BootstrapTests
{
    [Fact]
    public void App_assembly_is_referenced()
    {
        Assert.Equal("SpaceMaid", typeof(SpaceMaid.App.App).Assembly.GetName().Name);
    }
}
