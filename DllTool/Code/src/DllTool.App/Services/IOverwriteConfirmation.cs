namespace DllTool.App.Services;

/// <summary>
/// 覆盖确认抽象：UI层实现弹出确认弹窗，测试可注入替代实现。
/// </summary>
public interface IOverwriteConfirmation
{
    /// <summary>是否确认执行覆盖操作。</summary>
    bool Confirm();
}
