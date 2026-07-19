using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace ApexTrace.App.Pages;
public partial class RealtimePage : UserControl
{
    public RealtimePage() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        ApplyPanelRatio(viewModel.RealtimeTrackPanelRatio);
    }
    private void MainGridSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var total = TrackPanelColumn.ActualWidth + DataPanelColumn.ActualWidth;
        if (total <= 0 || DataContext is not MainViewModel viewModel) return;
        viewModel.SaveRealtimeTrackPanelRatio(TrackPanelColumn.ActualWidth / total);
    }
    private void ApplyPanelRatio(double ratio)
    {
        ratio = Math.Clamp(ratio, 0.28, 0.72);
        TrackPanelColumn.Width = new GridLength(ratio, GridUnitType.Star);
        DataPanelColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }
    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => RealtimeTrack.ZoomBy(1.2);
    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => RealtimeTrack.ZoomBy(0.82);
    private void Fit_OnClick(object sender, RoutedEventArgs e) => RealtimeTrack.FitToView();
}
