using System.Windows.Input;
using CodeMemo.Services;
using Xunit;

namespace CodeMemo.Tests;

/// <summary>键盘录制 → 手势文本：主键映射、Alt 的 Key.System 处理、非法输入拒绝。</summary>
public class HotkeyRecorderTests
{
    [Fact]
    public void 修饰键加字母()
    {
        Assert.Equal("Ctrl+Alt+C", HotkeyRecorder.FromKeyboard(Key.C, ModifierKeys.Control | ModifierKeys.Alt));
    }

    [Fact]
    public void 修饰键顺序固定为_Ctrl_Alt_Shift_Win()
    {
        Assert.Equal("Ctrl+Alt+Shift+K",
            HotkeyRecorder.FromKeyboard(Key.K, ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control));
        Assert.Equal("Alt+Win+C",
            HotkeyRecorder.FromKeyboard(Key.C, ModifierKeys.Windows | ModifierKeys.Alt));
    }

    [Fact]
    public void 功能键与数字键()
    {
        Assert.Equal("Alt+Shift+F5", HotkeyRecorder.FromKeyboard(Key.F5, ModifierKeys.Alt | ModifierKeys.Shift));
        Assert.Equal("Ctrl+7", HotkeyRecorder.FromKeyboard(Key.D7, ModifierKeys.Control));
        Assert.Equal("Ctrl+3", HotkeyRecorder.FromKeyboard(Key.NumPad3, ModifierKeys.Control));
        Assert.Equal("Win+Space", HotkeyRecorder.FromKeyboard(Key.Space, ModifierKeys.Windows));
    }

    [Fact]
    public void 只按修饰键或没有修饰键时返回空()
    {
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.LeftCtrl, ModifierKeys.Control));
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.RightAlt, ModifierKeys.Alt));
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.System, ModifierKeys.Alt));   // 未还原主键
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.C, ModifierKeys.None));       // 没有修饰键
    }

    [Fact]
    public void 不支持的主键返回空()
    {
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.Tab, ModifierKeys.Control));
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.Escape, ModifierKeys.Control));
        Assert.Null(HotkeyRecorder.FromKeyboard(Key.OemPlus, ModifierKeys.Control));
    }

    [Fact]
    public void 录出来的文本一定能被解析器接受()
    {
        var gestures = new[]
        {
            HotkeyRecorder.FromKeyboard(Key.C, ModifierKeys.Control | ModifierKeys.Alt),
            HotkeyRecorder.FromKeyboard(Key.F9, ModifierKeys.Shift),
            HotkeyRecorder.FromKeyboard(Key.Space, ModifierKeys.Alt),
            HotkeyRecorder.FromKeyboard(Key.D0, ModifierKeys.Windows),
            HotkeyRecorder.FromKeyboard(Key.Z, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows),
        };

        foreach (var gesture in gestures)
        {
            Assert.NotNull(gesture);
            Assert.True(HotkeyGesture.TryParse(gesture, out var parsed), gesture);
            Assert.Equal(gesture, parsed!.Text);   // 录制出来的写法已经就是规范化写法
        }
    }
}
