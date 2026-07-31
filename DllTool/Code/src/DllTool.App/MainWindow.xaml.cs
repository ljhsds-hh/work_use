using System.Windows;
using DllTool.App.ViewModels;

namespace DllTool.App;

/// <summary>
/// 主窗口。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void PickNewDllDirectory_Click(object sender, RoutedEventArgs e) => _viewModel.PickNewDllDirectory();
    private void PickManifestFile_Click(object sender, RoutedEventArgs e) => _viewModel.PickManifestFile();
    private void PickTargetDirectory_Click(object sender, RoutedEventArgs e) => _viewModel.PickTargetDirectory();
    private void PickBackgroundImage_Click(object sender, RoutedEventArgs e) => _viewModel.PickBackgroundImage();
    private void ClearBackgroundImage_Click(object sender, RoutedEventArgs e) => _viewModel.ClearBackgroundImage();
    private void PickBackupRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsModeA)
        {
            _viewModel.PickBackupRootA();
        }
        else
        {
            _viewModel.PickBackupRootB();
        }
    }
}
