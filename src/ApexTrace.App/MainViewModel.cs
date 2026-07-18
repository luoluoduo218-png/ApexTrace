using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using ApexTrace.Analysis;
using ApexTrace.Core;
using ApexTrace.Lmu;
using ApexTrace.Recording;
using ApexTrace.Setup;
using ApexTrace.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace ApexTrace.App;

public partial class MainViewModel : ObservableObject
{
    private readonly LmuInstallationProbe _probe;
    private readonly LmuDuckDbImporter _importer;
    private readonly ApexTracePackageService _packages;
    private readonly LocalSessionRepository _repository;
    private readonly DrivingEventDetector _eventDetector;
    private readonly EvidenceRecommendationEngine _recommendationEngine;
    private readonly DispatcherTimer _replayTimer;
    private CancellationTokenSource? _recordingCancellation;
    private Task? _captureTask;
    private SessionRecorder? _recorder;
    private readonly List<TelemetrySample> _liveSamples = [];

    public MainViewModel(
        LmuInstallationProbe probe,
        LmuDuckDbImporter importer,
        ApexTracePackageService packages,
        LocalSessionRepository repository,
        DrivingEventDetector eventDetector,
        EvidenceRecommendationEngine recommendationEngine)
    {
        _probe = probe;
        _importer = importer;
        _packages = packages;
        _repository = repository;
        _eventDetector = eventDetector;
        _recommendationEngine = recommendationEngine;
        Navigation =
        [
            new("⌂", "首页", PageKind.Home),
            new("◉", "实时采集", PageKind.Realtime),
            new("∞", "多圈总览", PageKind.MultiLap),
            new("▷", "单圈回放", PageKind.Replay),
            new("⌘", "双圈对比", PageKind.Compare),
            new("⌁", "弯道分析", PageKind.Corners),
            new("✣", "调教建议", PageKind.Setup),
            new("▣", "记录库", PageKind.Library),
            new("⚙", "设置", PageKind.Settings)
        ];
        _replayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, ReplayTick, Dispatcher.CurrentDispatcher);
        NavigateCommand = new RelayCommand<PageKind>(page => CurrentPage = page);
        ImportLatestCommand = new AsyncRelayCommand(ImportLatestAsync);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => CanStartRecording);
        EndRecordingCommand = new AsyncRelayCommand(EndRecordingAsync, () => IsRecording);
        ToggleReplayCommand = new RelayCommand(ToggleReplay, () => CurrentSession is not null);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => CurrentSession is not null);
        ExportDefaultCommand = new AsyncRelayCommand(ExportDefaultAsync, () => CurrentSession is not null);
        DiscardCommand = new AsyncRelayCommand(DiscardAsync);
    }

    public IReadOnlyList<NavigationItem> Navigation { get; }
    public IRelayCommand<PageKind> NavigateCommand { get; }
    public IAsyncRelayCommand ImportLatestCommand { get; }
    public IAsyncRelayCommand StartRecordingCommand { get; }
    public IAsyncRelayCommand EndRecordingCommand { get; }
    public IRelayCommand ToggleReplayCommand { get; }
    public IAsyncRelayCommand SaveSessionCommand { get; }
    public IAsyncRelayCommand ExportDefaultCommand { get; }
    public IAsyncRelayCommand DiscardCommand { get; }

    [ObservableProperty] private PageKind _currentPage = PageKind.Home;
    [ObservableProperty] private LmuConnectionStatus _connection = new(false, null, false, false, LmuInstallationProbe.DefaultInstallationPath, string.Empty, "Unknown", "正在检查 LMU…");
    [ObservableProperty] private TelemetrySession? _currentSession;
    [ObservableProperty] private TelemetrySample? _currentSample;
    [ObservableProperty] private SetupSnapshot _setupSnapshot = new(1, "Unavailable", string.Empty, [], "等待数据");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isEndModalVisible;
    [ObservableProperty] private double _replayProgress;
    [ObservableProperty] private string _statusMessage = "正在初始化…";
    [ObservableProperty] private string _lastSavedPath = string.Empty;
    [ObservableProperty] private ObservableCollection<SessionMetadata> _sessionLibrary = [];

    public bool CanStartRecording => Connection.SharedMemoryAvailable && Connection.HeaderSupported && !IsRecording;
    public string TrackName => CurrentSession?.Metadata.TrackName ?? "等待 LMU";
    public string VehicleName => CurrentSession?.Metadata.VehicleName ?? "未识别车辆";
    public string SessionType => CurrentSession?.Metadata.SessionType ?? "--";
    public string DataSourceText => CurrentSession?.Metadata.DataSource switch
    {
        TelemetryDataSource.LmuNativeDuckDb => "LMU 官方 DuckDB（只读）",
        TelemetryDataSource.LmuSharedMemory => "LMU_Data（只读）",
        TelemetryDataSource.ApexTracePackage => ".apextrace 包",
        _ => "等待数据"
    };
    public string ConnectionText => Connection.SharedMemoryAvailable ? "LMU 已连接" : Connection.ProcessDetected ? "LMU 已检测 / 等待接口" : "LMU 未运行";
    public string HeaderHashShort => string.IsNullOrEmpty(Connection.HeaderSha256) ? "--" : Connection.HeaderSha256[..Math.Min(16, Connection.HeaderSha256.Length)] + "…";
    public IReadOnlyList<TrackPoint> TrackPoints => CurrentSession?.Track.CenterLine ?? [];
    public IReadOnlyList<LapRecord> Laps => CurrentSession?.Laps ?? [];
    public IReadOnlyList<DrivingEvent> Events => CurrentSession?.Events ?? [];
    public IReadOnlyList<Recommendation> Recommendations => CurrentSession?.Recommendations ?? [];
    public int SampleCount => CurrentSession?.Samples.Count ?? 0;
    public int CompleteLapCount => CurrentSession?.Laps.Count(lap => lap.IsComplete) ?? 0;
    public string DurationText => FormatTime(CurrentSession?.DurationSeconds ?? 0);
    public string SpeedText => $"{(CurrentSample?.SpeedMetersPerSecond ?? 0) * 3.6:F0}";
    public string GearText => CurrentSample?.Gear switch { -1 => "R", 0 => "N", int gear => gear.ToString(), _ => "--" };
    public string RpmText => $"{CurrentSample?.EngineRpm ?? 0:F0}";
    public double ThrottleValue
    {
        get => CurrentSample?.Throttle ?? 0;
        set { }
    }
    public double BrakeValue
    {
        get => CurrentSample?.Brake ?? 0;
        set { }
    }
    public string ThrottleText => $"{ThrottleValue:P0}";
    public string BrakeText => $"{BrakeValue:P0}";
    public string SteeringText => $"{(CurrentSample?.Steering ?? 0) * 540:F0}°";
    public string FuelText => $"{CurrentSample?.FuelLiters ?? 0:F1} L";
    public string LapDistanceText => $"{CurrentSample?.LapDistanceMeters ?? 0:F1} m";
    public string CurrentLapText => CurrentSample is null ? "--" : CurrentSample.LapNumber.ToString();
    public string AmbientText => $"{CurrentSample?.Environment.AmbientTemperatureCelsius ?? 0:F1}°C";
    public string TrackTemperatureText => $"{CurrentSample?.Environment.TrackTemperatureCelsius ?? 0:F1}°C";
    public string CompletenessText => CurrentSession?.Metadata.CompletenessDiagnostic ?? "等待导入或实时采集";
    public string ImportSummary => CurrentSession is null ? "尚无遥测" : $"{SampleCount:N0} 样本 · {DurationText} · {CurrentSession.Track.CenterLine.Count:N0} 轨迹点";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            Connection = _probe.Probe();
            StatusMessage = Connection.Diagnostic;
            await ImportLatestAsync();
            var saved = await _repository.ListAsync();
            SessionLibrary = new ObservableCollection<SessionMetadata>(saved);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "ApexTrace initialization failed");
            StatusMessage = $"初始化失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    public async Task ImportPathAsync(string path)
    {
        IsBusy = true;
        StatusMessage = "正在只读导入 LMU 原生 DuckDB…";
        try
        {
            var session = await _importer.ImportAsync(path);
            var detectedEvents = _eventDetector.Detect(session.Samples);
            session = session with { Events = detectedEvents };
            session = session with { Recommendations = _recommendationEngine.Analyze(session) };
            CurrentSession = session;
            CurrentSample = session.Samples.LastOrDefault();
            SetupSnapshot = SetupSnapshotProbe.FromLmuMetadataJson(session.Metadata.SetupJson);
            ReplayProgress = 1;
            StatusMessage = $"已导入真实 LMU 数据：{session.Samples.Count:N0} 样本。{session.Metadata.CompletenessDiagnostic}";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "LMU DuckDB import failed for {Path}", path);
            StatusMessage = $"导入失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshAll();
        }
    }

    public async Task ExportToAsync(string path)
    {
        if (CurrentSession is null) return;
        IsBusy = true;
        try
        {
            LastSavedPath = await _packages.ExportAsync(CurrentSession, path);
            StatusMessage = $"已导出：{LastSavedPath}";
            IsEndModalVisible = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportLatestAsync()
    {
        var files = LmuInstallationProbe.FindNativeTelemetryFiles(Connection.InstallationPath);
        var usable = files.Select(path => new FileInfo(path)).Where(file => file.Length > 1_000_000).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
        if (usable is null)
        {
            StatusMessage = Connection.Diagnostic + " 未发现非空 LMU DuckDB 遥测。";
            return;
        }
        await ImportPathAsync(usable.FullName);
    }

    private async Task StartRecordingAsync()
    {
        if (!CanStartRecording) return;
        IsRecording = true;
        CurrentPage = PageKind.Realtime;
        StatusMessage = "正在通过只读 LMU_Data 采集…";
        _liveSamples.Clear();
        _recordingCancellation = new CancellationTokenSource();
        _recorder = new SessionRecorder();
        var metadata = new SessionMetadata(1, Guid.NewGuid(), TrackName, TrackName, VehicleName, string.Empty, SessionType,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TelemetryDataSource.LmuSharedMemory, "LMU_Data", null,
            Connection.HeaderSha256, false, "实时记录尚未结束", null);
        var tempRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexTrace", "Temp");
        await _recorder.StartAsync(tempRoot, metadata, _recordingCancellation.Token);
        var reader = new LmuSharedMemoryReader(Connection);
        _captureTask = Task.Run(async () =>
        {
            await using (reader)
            await foreach (var sample in reader.ReadAllAsync(_recordingCancellation.Token))
            {
                _liveSamples.Add(sample);
                await _recorder.EnqueueAsync(sample, _recordingCancellation.Token);
                await App.Current.Dispatcher.InvokeAsync(() => CurrentSample = sample, DispatcherPriority.Render);
            }
        }, _recordingCancellation.Token);
        RefreshCommands();
    }

    private async Task EndRecordingAsync()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _recordingCancellation?.Cancel();
        try { if (_captureTask is not null) await _captureTask; } catch (OperationCanceledException) { }
        if (_recorder is not null) await _recorder.FinishAsync();
        if (_liveSamples.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var baseSession = CurrentSession;
            var metadata = new SessionMetadata(1, Guid.NewGuid(), TrackName, TrackName, VehicleName, string.Empty, SessionType,
                _liveSamples[0].CapturedAtUtc, now, TelemetryDataSource.LmuSharedMemory, "LMU_Data", null,
                Connection.HeaderSha256, false, "实时记录已结束；最后一圈标记为不完整，等待分圈校验。", null);
            var track = baseSession?.Track ?? new TrackDefinition(1, TrackName, "runtime", TrackGeometrySource.RuntimeReconstructionPartial,
                null, string.Empty, [], "实时运行时重建");
            CurrentSession = new TelemetrySession(1, metadata, _liveSamples.ToArray(), [], track, _eventDetector.Detect(_liveSamples), []);
        }
        IsEndModalVisible = true;
        RefreshAll();
    }

    private void ToggleReplay()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying) _replayTimer.Start(); else _replayTimer.Stop();
    }

    private void ReplayTick(object? sender, EventArgs e)
    {
        if (CurrentSession is null || CurrentSession.Samples.Count == 0) return;
        ReplayProgress += 1.0 / Math.Max(1, CurrentSession.DurationSeconds * 60);
        if (ReplayProgress >= 1)
        {
            ReplayProgress = 0;
        }
    }

    partial void OnReplayProgressChanged(double value)
    {
        if (CurrentSession?.Samples.Count > 0)
        {
            var index = Math.Clamp((int)Math.Round(value * (CurrentSession.Samples.Count - 1)), 0, CurrentSession.Samples.Count - 1);
            CurrentSample = CurrentSession.Samples[index];
        }
    }

    partial void OnCurrentSampleChanged(TelemetrySample? value) => RefreshTelemetry();
    partial void OnCurrentSessionChanged(TelemetrySession? value) => RefreshAll();
    partial void OnConnectionChanged(LmuConnectionStatus value) => RefreshAll();
    partial void OnIsRecordingChanged(bool value) => RefreshCommands();

    private async Task SaveSessionAsync()
    {
        if (CurrentSession is null) return;
        LastSavedPath = await _repository.SaveAsync(CurrentSession);
        StatusMessage = $"已保存到本地记录库：{LastSavedPath}";
        IsEndModalVisible = false;
        SessionLibrary = new ObservableCollection<SessionMetadata>(await _repository.ListAsync());
    }

    private Task ExportDefaultAsync()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ApexTrace", "Exports");
        var path = Path.Combine(directory, $"ApexTrace_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.apextrace");
        return ExportToAsync(path);
    }

    private async Task DiscardAsync()
    {
        if (_recorder?.SessionDirectory is { } directory)
        {
            var expectedRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexTrace", "Temp"));
            var target = Path.GetFullPath(directory);
            if (target.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }
        IsEndModalVisible = false;
        StatusMessage = "本次临时记录已丢弃。";
        await Task.CompletedTask;
    }

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(TrackName));
        OnPropertyChanged(nameof(VehicleName));
        OnPropertyChanged(nameof(SessionType));
        OnPropertyChanged(nameof(DataSourceText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(HeaderHashShort));
        OnPropertyChanged(nameof(TrackPoints));
        OnPropertyChanged(nameof(Laps));
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(Recommendations));
        OnPropertyChanged(nameof(SampleCount));
        OnPropertyChanged(nameof(CompleteLapCount));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(CompletenessText));
        OnPropertyChanged(nameof(ImportSummary));
        RefreshTelemetry();
        RefreshCommands();
    }

    private void RefreshTelemetry()
    {
        foreach (var property in new[] { nameof(SpeedText), nameof(GearText), nameof(RpmText), nameof(ThrottleValue), nameof(BrakeValue),
                     nameof(ThrottleText), nameof(BrakeText), nameof(SteeringText), nameof(FuelText), nameof(LapDistanceText),
                     nameof(CurrentLapText), nameof(AmbientText), nameof(TrackTemperatureText) })
        {
            OnPropertyChanged(property);
        }
    }

    private void RefreshCommands()
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        EndRecordingCommand.NotifyCanExecuteChanged();
        ToggleReplayCommand.NotifyCanExecuteChanged();
        SaveSessionCommand.NotifyCanExecuteChanged();
        ExportDefaultCommand.NotifyCanExecuteChanged();
    }

    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff");
}
