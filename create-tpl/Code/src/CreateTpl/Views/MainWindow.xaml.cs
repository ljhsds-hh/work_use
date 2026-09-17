using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CreateTpl.Services;
using CreateTpl.ViewModels;

namespace CreateTpl.Views;

/// <summary>
/// 主窗口：视图装配与轻量视图层交互（行号滚动同步），业务逻辑全部在 MainViewModel。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 轻量工具直接构造依赖；如后续扩展可替换为 IoC 容器注入
        DataContext = new MainViewModel(
            new ProjectTemplateService(),
            new FolderPickerService(),
            new GrowlMessageService());
    }

    /// <summary>
    /// 编辑器装载后找到其内部 ScrollViewer，将滚动事件转发给行号栏（视图层专属行为）。
    /// </summary>
    private void NamesBoxLoaded(object sender, RoutedEventArgs e)
    {
        var editor = (TextBox)sender;
        var viewer = FindScrollViewer(editor);

        if (viewer != null)
        {
            viewer.ScrollChanged += (_, args) =>
                GutterScroll.ScrollToVerticalOffset(editor.VerticalOffset);
        }
    }

    /// <summary>在可视化树中向下查找 TextBox 内部的 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            var result = FindScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

