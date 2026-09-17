namespace CreateTpl.Services;

/// <summary>目录选择器抽象：隔离 WPF 对话框依赖，保持 ViewModel 可测试。</summary>
public interface IFolderPickerService
{
    /// <summary>打开目录选择对话框；返回所选目录路径，用户取消返回 null。</summary>
    string? PickFolder(string? initialDirectory = null);
}
