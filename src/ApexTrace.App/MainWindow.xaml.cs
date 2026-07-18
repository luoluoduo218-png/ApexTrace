using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApexTrace.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        var argument = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--capture-ui=", StringComparison.OrdinalIgnoreCase));
        if (argument is not null)
        {
            await CaptureAllPagesAsync(argument[13..].Trim('"'));
            Close();
        }
    }

    private async Task CaptureAllPagesAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var pages = new[]
        {
            (PageKind.Realtime, "01_realtime_capture.png"), (PageKind.Home, "02_home_ready.png"),
            (PageKind.MultiLap, "03_multi_lap_overview.png"), (PageKind.Replay, "04_single_lap_replay.png"),
            (PageKind.Compare, "05_two_lap_compare.png"), (PageKind.Corners, "06_corner_analysis.png"),
            (PageKind.Setup, "07_setup_recommendations.png"), (PageKind.Library, "09_session_library.png"),
            (PageKind.Settings, "10_settings.png")
        };
        foreach (var (page, file) in pages)
        {
            _viewModel.CurrentPage = page;
            _viewModel.IsEndModalVisible = false;
            await WaitForLayoutAsync();
            Capture(Path.Combine(outputDirectory, file));
        }
        _viewModel.CurrentPage = PageKind.Realtime;
        _viewModel.IsEndModalVisible = true;
        await WaitForLayoutAsync();
        Capture(Path.Combine(outputDirectory, "08_end_session_modal.png"));
    }

    private async Task WaitForLayoutAsync()
    {
        await Dispatcher.InvokeAsync(() => { UpdateLayout(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
    }

    private void Capture(string path)
    {
        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        var bitmap = new RenderTargetBitmap((int)(ActualWidth * dpiX), (int)(ActualHeight * dpiY), 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
