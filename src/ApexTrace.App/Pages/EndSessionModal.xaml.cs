using System.Windows;
using System.Windows.Controls;

namespace ApexTrace.App.Pages;

public partial class EndSessionModal : UserControl
{
    public EndSessionModal() => InitializeComponent();

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var result = MessageBox.Show("临时记录将被删除，且无法恢复。确定放弃吗？", "放弃本次记录", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result == MessageBoxResult.Yes && viewModel.DiscardCommand.CanExecute(null))
            viewModel.DiscardCommand.Execute(null);
    }
}
