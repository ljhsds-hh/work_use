using QuietRemind.Helpers;
using System.Windows;
using QuietRemind.ViewModels;

namespace QuietRemind.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.EnableRoundedCorners(this);
        var work = SystemParameters.WorkArea;
        Width = Math.Max(720, Math.Min(860, work.Width - 48));
        Height = Math.Max(460, Math.Min(640, work.Height - 48));
        DataContext = vm;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
