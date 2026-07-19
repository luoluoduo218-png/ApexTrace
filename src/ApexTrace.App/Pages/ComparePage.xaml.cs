using System.Windows;
using System.Windows.Controls;
namespace ApexTrace.App.Pages;
public partial class ComparePage : UserControl
{
    public ComparePage() => InitializeComponent();

    private void ExpandChart_OnClick(object sender, RoutedEventArgs e)
    {
        var showPedals = sender is Button { Tag: string tag }
            && string.Equals(tag, "Pedals", StringComparison.Ordinal);
        ExpandedTitle.Text = showPedals ? "踏板输入对比" : "速度对比";
        ExpandedHint.Text = showPedals ? "实线油门 / 虚线刹车" : string.Empty;
        ExpandedChart.ShowPedals = showPedals;
        ExpandedOverlay.Visibility = Visibility.Visible;
    }

    private void CloseExpandedChart_OnClick(object sender, RoutedEventArgs e) =>
        ExpandedOverlay.Visibility = Visibility.Collapsed;
}
