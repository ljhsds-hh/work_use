using System.IO;
using Microsoft.Win32;

namespace CreateTpl.Services;

/// <summary>
/// 目录选择器实现：使用 .NET 8 WPF 内置 OpenFolderDialog。
/// HandyControl 无目录选择组件，此处按需求 2 规范采用原生控件兜底。
/// </summary>
public class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择工程根目录",
            InitialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
