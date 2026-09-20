using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>全局热键手势解析：合法写法的规范化 + 非法写法的拒绝。</summary>
public class HotkeyGestureTests
{
    [Fact]
    public void 解析标准写法()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+C", out var gesture));

        Assert.NotNull(gesture);
        Assert.Equal(HotkeyGesture.ModControl | HotkeyGesture.ModAlt, gesture!.Modifiers);
        Assert.Equal((uint)'C', gesture.VirtualKey);
        Assert.Equal("Ctrl+Alt+C", gesture.Text);
    }

    [Theory]
    [InlineData("ctrl+alt+c", "Ctrl+Alt+C")]
    [InlineData("  ALT + CTRL + c  ", "Ctrl+Alt+C")]
    [InlineData("control+shift+win+F5", "Ctrl+Shift+Win+F5")]
    [InlineData("Win+1", "Win+1")]
    public void 写法归一化(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(expected, gesture!.Text);
    }

    [Fact]
    public void F_键映射到虚拟键码()
    {
        Assert.True(HotkeyGesture.TryParse("Shift+F5", out var gesture));

        Assert.Equal(HotkeyGesture.ModShift, gesture!.Modifiers);
        Assert.Equal(0x74u, gesture.VirtualKey);   // VK_F5
    }

    [Fact]
    public void 数字键映射到字符码()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+1", out var gesture));
        Assert.Equal((uint)'1', gesture!.VirtualKey);
    }

    [Fact]
    public void 空格键()
    {
        Assert.True(HotkeyGesture.TryParse("Alt+Space", out var gesture));

        Assert.Equal(HotkeyGesture.ModAlt, gesture!.Modifiers);
        Assert.Equal(HotkeyGesture.VkSpace, gesture.VirtualKey);
        Assert.Equal("Alt+Space", gesture.Text);

        Assert.True(HotkeyGesture.TryParse("win+space", out var lower));
        Assert.Equal("Win+Space", lower!.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C")]                 // 缺修饰键
    [InlineData("Ctrl")]              // 缺主键
    [InlineData("Ctrl+")]             // 主键为空
    [InlineData("Ctrl++C")]           // 中间有空片段
    [InlineData("Ctrl+Alt+鼠标")]      // 主键不认识
    [InlineData("Ctrl+Enter")]        // 主键不在支持范围
    [InlineData("Ctrl+F25")]          // 超出 F1-F24
    [InlineData("Ctrl+Ctrl+C")]       // 修饰键重复
    [InlineData("Fn+C")]              // 修饰键不认识
    public void 非法写法一律拒绝(string? input)
    {
        Assert.False(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Null(gesture);
    }
}
