using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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
        LocalizationBehavior.Enable(this);
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.CurrentPage) or nameof(MainViewModel.SettingsSection))
                Dispatcher.BeginInvoke(() => LocalizationBehavior.Refresh(this), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var work = FindDisplay1WorkArea();
        if (work is null) return;

        var width = Math.Min(1672, work.Value.Right - work.Value.Left - 48);
        var height = Math.Min(941, work.Value.Bottom - work.Value.Top - 48);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, work.Value.Left + 24, work.Value.Top + 24, width, height,
            SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    private static NativeRect? FindDisplay1WorkArea()
    {
        NativeRect? display1 = null;
        NativeRect? primary = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            if ((info.Flags & MonitorInfoPrimary) != 0) primary = info.WorkArea;
            if (string.Equals(info.DeviceName, @"\\.\DISPLAY1", StringComparison.OrdinalIgnoreCase))
                display1 = info.WorkArea;
            return true;
        }, IntPtr.Zero);
        return display1 ?? primary;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        var arguments = Environment.GetCommandLineArgs();
        var captureLanguage = arguments.FirstOrDefault(value => value.StartsWith("--capture-language=", StringComparison.OrdinalIgnoreCase));
        if (captureLanguage is not null)
        {
            LocalizationManager.SetLanguage(captureLanguage[19..].Trim('"'));
        }
        if (arguments.Any(value => string.Equals(value, "--recover-latest", StringComparison.OrdinalIgnoreCase)))
        {
            await _viewModel.RecoverLatestCommand.ExecuteAsync(null);
            _viewModel.CurrentPage = PageKind.Setup;
        }

        if (arguments.Any(value => string.Equals(value, "--start-live", StringComparison.OrdinalIgnoreCase)))
        {
            await _viewModel.StartRecordingCommand.ExecuteAsync(null);
            _viewModel.CurrentPage = PageKind.Realtime;
        }

        var argument = arguments.FirstOrDefault(value => value.StartsWith("--capture-ui=", StringComparison.OrdinalIgnoreCase));
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
            (PageKind.Compare, "05_two_lap_compare.png"), (PageKind.Setup, "07_setup_recommendations.png"),
            (PageKind.Library, "09_session_library.png"),
            (PageKind.Settings, "10_settings.png")
        };
        foreach (var (page, file) in pages)
        {
            _viewModel.CurrentPage = page;
            _viewModel.IsEndModalVisible = false;
            await WaitForLayoutAsync();
            Capture(Path.Combine(outputDirectory, file));
        }
        _viewModel.CurrentPage = PageKind.Settings;
        _viewModel.SettingsSection = "Display";
        await WaitForLayoutAsync();
        Capture(Path.Combine(outputDirectory, "10_settings_display.png"));
        _viewModel.SettingsSection = "Storage";
        await WaitForLayoutAsync();
        Capture(Path.Combine(outputDirectory, "10_settings_storage.png"));
        _viewModel.SettingsSection = "About";
        await WaitForLayoutAsync();
        Capture(Path.Combine(outputDirectory, "11_settings_about.png"));
        _viewModel.CurrentPage = PageKind.Realtime;
        _viewModel.IsEndModalVisible = true;
        await WaitForLayoutAsync();
        Capture(Path.Combine(outputDirectory, "08_end_session_modal.png"));
    }

    private async Task WaitForLayoutAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateLayout();
            LocalizationBehavior.Refresh(this);
            UpdateLayout();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint MonitorInfoPrimary = 0x0001;

    private delegate bool MonitorEnumCallback(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumCallback callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
