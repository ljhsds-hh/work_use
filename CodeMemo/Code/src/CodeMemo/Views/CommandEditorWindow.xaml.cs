using System.Windows;
using CodeMemo.Models;
using CodeMemo.Services;
using CodeMemo.ViewModels;

namespace CodeMemo.Views;

/// <summary>命令新增 / 编辑窗口：ShowDialog 返回 true 表示已保存。</summary>
public partial class CommandEditorWindow : Window
{
    private readonly CommandEditorViewModel _viewModel;

    public CommandEditorWindow(CommandEntry? original, string? defaultCategory, string? defaultGroup, IDialogService dialogs)
    {
        InitializeComponent();
        _viewModel = new CommandEditorViewModel(original, defaultCategory, defaultGroup, dialogs);
        DataContext = _viewModel;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 校验不过时由 VM 弹提示，窗口不关闭
        _viewModel.Confirmed = entry =>
        {
            SavedEntry = entry;
            DialogResult = true;
            Close();
        };
        _viewModel.Confirm();
    }

    /// <summary>保存成功后的条目（新增或编辑后）。</summary>
    public CommandEntry? SavedEntry { get; private set; }
}
