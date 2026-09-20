using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using HandyControl.Controls;

namespace SpaceMaid.App.Services;

/// <summary>
/// 系统"选择文件夹"能力（实现在 <see cref="FolderPickerService"/>：Win32 SHBrowseForFolder）。
/// 抽成接口是为了让设置页 ViewModel 能脱离 UI 单测。
/// </summary>
public interface IFolderPicker
{
    /// <summary>弹出系统文件夹选择框；用户取消返回 null。</summary>
    string? PickFolder(string title, string? initialDirectory);
}

/// <summary>
/// 二次确认与提示。
/// 设计约束（需求 4.3-1 / 3.9）：危险动作的确认文案必须写清"会发生什么"，因此这里的
/// <see cref="Confirm"/> 只负责"显示并取回用户的决定"，**文案由 ViewModel 提供**（可被单测断言）。
/// </summary>
public interface IDialogService
{
    /// <summary>二次确认；返回用户是否点了「确定」。</summary>
    bool Confirm(string message, string title);

    /// <summary>只提示不阻塞流程（例如执行前发现的问题）。</summary>
    void Warn(string message, string title);
}

/// <summary>轻提示（Growl）。做成接口以便测试里断言"提示了什么"。</summary>
public interface INotificationService
{
    /// <summary>一条普通提示。</summary>
    void Notify(string message);
}

/// <summary>系统外壳（打开目录）。ViewModel 不直接碰 Process。</summary>
public interface IShellService
{
    void OpenDirectory(string path);
}

/// <summary>
/// WPF 对话框实现。用 HandyControl 的 <see cref="HandyControl.Controls.MessageBox"/>（与皮肤一致），
/// 默认按钮固定为「取消」——危险动作的回车路径必须是"不执行"。
/// </summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string message, string title)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? HandyControl.Controls.MessageBox.Show(
                message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            : HandyControl.Controls.MessageBox.Show(
                owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }

    public void Warn(string message, string title)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            HandyControl.Controls.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
        }
        else
        {
            HandyControl.Controls.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
        }
    }
}

/// <summary>
/// Growl 轻提示。token 与主窗里 hc:Growl 面板上的 Token 必须一致（在 MainWindow 里配对注册）。
/// </summary>
public sealed class GrowlNotificationService(string token) : INotificationService
{
    public string Token { get; } = token;

    public void Notify(string message) => Growl.Info(message, Token);
}

/// <summary>资源管理器打开目录；目录不存在时先建出来，避免打开报错。</summary>
public sealed class ShellService : IShellService
{
    public void OpenDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            // 建不出来就直接尝试打开，让系统给出真实错误
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}

/// <summary>把异常/提示文案统一成"人能看懂"的一行（界面状态栏与 Growl 共用）。</summary>
public static class MessageText
{
    public static string OneLine(string? message) =>
        string.IsNullOrWhiteSpace(message) ? string.Empty : message.Replace("\r", " ").Replace("\n", " ").Trim();

    public static string Join(IEnumerable<string> parts, string separator = "；") =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string Truncate(string text, int max = 300) =>
        text.Length <= max ? text : text[..max] + "…";

    public static string BuildSummary(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }
}
