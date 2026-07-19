using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using ApexTrace.Analysis;
using ApexTrace.Core;
using ApexTrace.Lmu;
using ApexTrace.Recording;
using ApexTrace.Setup;
using ApexTrace.Storage;
using ApexTrace.Track;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Microsoft.VisualBasic.FileIO;

namespace ApexTrace.App;

public partial class MainViewModel : ObservableObject
{
    private readonly LmuInstallationProbe _probe;
    private readonly LmuDuckDbImporter _importer;
    private readonly ApexTracePackageService _packages;
    private readonly LocalSessionRepository _repository;
    private readonly DrivingEventDetector _eventDetector;
    private readonly EvidenceRecommendationEngine _recommendationEngine;
    private readonly SessionAnalysisEngine _sessionAnalysisEngine;
    private readonly AppPreferencesStore _preferencesStore;
    private AppPreferences _preferences;
    private readonly DispatcherTimer _replayTimer;
    private readonly DispatcherTimer _connectionTimer;
    private long _lastReplayTickTimestamp;
    private CancellationTokenSource? _recordingCancellation;
    private Task? _captureTask;
    private SessionRecorder? _recorder;
    private readonly ConcurrentQueue<TelemetrySample> _liveSamples = new();
    private readonly List<TrackPoint> _liveTrackPoints = [];
    private IReadOnlyList<TrackPoint> _liveTrackPointsSnapshot = [];
    private readonly List<TelemetrySample> _livePedalTraceSamples = [];
    private IReadOnlyList<TelemetrySample> _livePedalTraceSnapshot = [];
    private LmuSessionContext? _liveContext;
    private Exception? _captureException;
    private DateTimeOffset _lastLiveSampleAtUtc;
    private int? _liveTrackLapNumber;
    private IReadOnlyList<TelemetrySample> _replaySamples = [];
    private IReadOnlyList<TrackPoint> _replayTrackPoints = [];
    private IReadOnlyList<DrivingEvent> _replayEvents = [];
    private IReadOnlyList<LapRecord> _liveLaps = [];
    private IReadOnlyList<TrackPoint> _completedLiveTrackPoints = [];
    private IReadOnlyList<MultiLapRow> _multiLapRows = [];
    private IReadOnlyList<LapTrace> _multiLapTraces = [];
    private IReadOnlyList<LapHistogramBar> _lapHistogramBars = [];
    private readonly double?[] _liveSectorTimes = new double?[3];
    private double _currentLapStartedAtSeconds;
    private double _fuelPerLap;
    private bool _suppressComparisonSelection;

    public MainViewModel(
        LmuInstallationProbe probe,
        LmuDuckDbImporter importer,
        ApexTracePackageService packages,
        LocalSessionRepository repository,
        DrivingEventDetector eventDetector,
        EvidenceRecommendationEngine recommendationEngine,
        SessionAnalysisEngine sessionAnalysisEngine,
        AppPreferencesStore preferencesStore)
    {
        _probe = probe;
        _importer = importer;
        _packages = packages;
        _repository = repository;
        _eventDetector = eventDetector;
        _recommendationEngine = recommendationEngine;
        _sessionAnalysisEngine = sessionAnalysisEngine;
        _preferencesStore = preferencesStore;
        _preferences = _preferencesStore.Load();
        LocalizationManager.SetLanguage(_preferences.Language, notify: false);
        var configuredInstallationPath = string.IsNullOrWhiteSpace(_preferences.LmuInstallationPath)
            ? LmuInstallationProbe.DefaultInstallationPath
            : _preferences.LmuInstallationPath;
        _connection = _connection with { InstallationPath = configuredInstallationPath };
        _isAxisPedalDisplay = _preferences.UseAxisPedalDisplay;
        Navigation =
        [
            new("⌂", "首页", PageKind.Home),
            new("◉", "实时采集", PageKind.Realtime),
            new("∞", "多圈总览", PageKind.MultiLap),
            new("▷", "单圈回放", PageKind.Replay),
            new("⌘", "双圈对比", PageKind.Compare),
            new("✣", "调教建议", PageKind.Setup),
            new("▣", "记录库", PageKind.Library),
            new("⚙", "设置", PageKind.Settings)
        ];
        _replayTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, ReplayTick, Dispatcher.CurrentDispatcher);
        _connectionTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, ConnectionTick, Dispatcher.CurrentDispatcher);
        _connectionTimer.Start();
        NavigateCommand = new RelayCommand<PageKind>(page => CurrentPage = page);
        ImportLatestCommand = new AsyncRelayCommand(ImportLatestAsync);
        RecoverLatestCommand = new AsyncRelayCommand(RecoverLatestAsync, () => RecoverableSessionCount > 0 && !IsRecording && !IsBusy);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => CanStartRecording);
        EndRecordingCommand = new AsyncRelayCommand(EndRecordingAsync, () => CanEndRecording);
        ToggleReplayCommand = new RelayCommand(ToggleReplay, () => CurrentSession is not null);
        StepReplayCommand = new RelayCommand<string>(direction => StepReplay(int.TryParse(direction, out var value) ? value : 0),
            _ => CurrentSession?.Samples.Count > 0);
        SeekReplayCommand = new RelayCommand<string>(seconds => SeekReplay(double.TryParse(seconds, out var value) ? value : 0),
            _ => ReplaySamples.Count > 1);
        SetPlaybackRateCommand = new RelayCommand<string>(rate => PlaybackRate = double.TryParse(rate, out var value) ? value : 1,
            _ => ReplaySamples.Count > 1);
        JumpToEventCommand = new RelayCommand<string>(direction => JumpToEvent(int.TryParse(direction, out var value) ? value : 0),
            _ => ReplayEvents.Count > 0);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => CurrentSession is not null);
        ExportDefaultCommand = new AsyncRelayCommand(ExportDefaultAsync, () => CurrentSession is not null);
        DiscardCommand = new AsyncRelayCommand(DiscardAsync);
        SelectSettingsSectionCommand = new RelayCommand<string>(section => SettingsSection = section ?? "Connection");
        OpenStorageFolderCommand = new RelayCommand(OpenStorageFolder);
        OpenSelectedSessionCommand = new AsyncRelayCommand(OpenSelectedSessionAsync, () => SelectedStoredSession is not null && !IsBusy);
        ExportSelectedSessionCommand = new AsyncRelayCommand(ExportSelectedSessionAsync, () => SelectedStoredSession is not null && !IsBusy);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync, () => SelectedStoredSession is not null && !IsBusy);
        RefreshLibraryCommand = new AsyncRelayCommand(() => RefreshLibraryAsync(), () => !IsBusy);
        SwapComparisonLapsCommand = new RelayCommand(SwapComparisonLaps);
        ResetLibraryFiltersCommand = new RelayCommand(ResetLibraryFilters);
    }

    public IReadOnlyList<NavigationItem> Navigation { get; }
    public IRelayCommand<PageKind> NavigateCommand { get; }
    public IAsyncRelayCommand ImportLatestCommand { get; }
    public IAsyncRelayCommand RecoverLatestCommand { get; }
    public IAsyncRelayCommand StartRecordingCommand { get; }
    public IAsyncRelayCommand EndRecordingCommand { get; }
    public IRelayCommand ToggleReplayCommand { get; }
    public IRelayCommand<string> StepReplayCommand { get; }
    public IRelayCommand<string> SeekReplayCommand { get; }
    public IRelayCommand<string> SetPlaybackRateCommand { get; }
    public IRelayCommand<string> JumpToEventCommand { get; }
    public IAsyncRelayCommand SaveSessionCommand { get; }
    public IAsyncRelayCommand ExportDefaultCommand { get; }
    public IAsyncRelayCommand DiscardCommand { get; }
    public IRelayCommand<string> SelectSettingsSectionCommand { get; }
    public IRelayCommand OpenStorageFolderCommand { get; }
    public IAsyncRelayCommand OpenSelectedSessionCommand { get; }
    public IAsyncRelayCommand ExportSelectedSessionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedSessionCommand { get; }
    public IAsyncRelayCommand RefreshLibraryCommand { get; }
    public IRelayCommand SwapComparisonLapsCommand { get; }
    public IRelayCommand ResetLibraryFiltersCommand { get; }

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
    [ObservableProperty] private double _playbackRate = 1;
    [ObservableProperty] private string _statusMessage = "正在初始化…";
    [ObservableProperty] private string _lastSavedPath = string.Empty;
    [ObservableProperty] private ObservableCollection<SessionMetadata> _sessionLibrary = [];
    [ObservableProperty] private ObservableCollection<StoredSessionInfo> _storedSessions = [];
    [ObservableProperty] private ObservableCollection<StoredSessionInfo> _filteredStoredSessions = [];
    [ObservableProperty] private StoredSessionInfo? _selectedStoredSession;
    [ObservableProperty] private int _recoverableSessionCount;
    [ObservableProperty] private string _settingsSection = "Connection";
    [ObservableProperty] private bool _isAxisPedalDisplay;
    [ObservableProperty] private LapComparisonResult _lapComparison = new(false, "等待完整圈", null, null, [], [], []);
    [ObservableProperty] private IReadOnlyList<CornerAnalysisResult> _cornerAnalyses = [];
    [ObservableProperty] private CornerAnalysisResult? _selectedCorner;
    [ObservableProperty] private LapRecord? _selectedReplayLap;
    [ObservableProperty] private MultiLapRow? _selectedMultiLapRow;
    [ObservableProperty] private LapRecord? _selectedComparisonCurrentLap;
    [ObservableProperty] private LapRecord? _selectedComparisonReferenceLap;
    [ObservableProperty] private string _librarySearchText = string.Empty;
    [ObservableProperty] private string _selectedLibraryTrackFilter = "全部赛道";
    [ObservableProperty] private string _selectedLibraryVehicleFilter = "全部车辆";
    [ObservableProperty] private string _selectedLibraryTimeFilter = "全部时间";
    [ObservableProperty] private string _selectedLibrarySessionFilter = "全部会话";
    [ObservableProperty] private string _selectedLibraryValidityFilter = "全部";
    [ObservableProperty] private ObservableCollection<string> _libraryTrackFilters = ["全部赛道"];
    [ObservableProperty] private ObservableCollection<string> _libraryVehicleFilters = ["全部车辆"];
    [ObservableProperty] private ObservableCollection<string> _librarySessionFilters = ["全部会话"];

    public bool CanStartRecording => Connection.SharedMemoryAvailable && Connection.HeaderSupported && !IsRecording && !IsBusy;
    public bool CanEndRecording => IsRecording && !IsBusy;
    public string TrackName => CurrentSession?.Metadata.TrackName ?? "等待 LMU";
    public string VehicleName => CurrentSession?.Metadata.VehicleName ?? "未识别车辆";
    public string SessionType => LocalizeSessionType(IsRecording
        ? _liveContext?.SessionType ?? CurrentSession?.Metadata.SessionType
        : CurrentSession?.Metadata.SessionType);
    public string DataSourceText => CurrentSession?.Metadata.DataSource switch
    {
        TelemetryDataSource.LmuNativeDuckDb => "LMU 官方 DuckDB（只读）",
        TelemetryDataSource.LmuSharedMemory => "LMU_Data（只读）",
        TelemetryDataSource.ApexTracePackage => ".apextrace 包",
        _ => "等待数据"
    };
    public string ConnectionText => Connection.SharedMemoryAvailable ? "LMU 已连接" : Connection.ProcessDetected ? "LMU 已检测 / 等待接口" : "LMU 未运行";
    public string HeaderHashShort => string.IsNullOrEmpty(Connection.HeaderSha256) ? "--" : Connection.HeaderSha256[..Math.Min(16, Connection.HeaderSha256.Length)] + "…";
    public IReadOnlyList<TrackPoint> TrackPoints => IsRecording ? _liveTrackPointsSnapshot : CurrentSession?.Track.CenterLine ?? [];
    public IReadOnlyList<TrackPoint> RealtimeTrackPoints => IsRecording && _completedLiveTrackPoints.Count > 1
        ? _completedLiveTrackPoints
        : TrackPoints;
    public double RealtimeTrackProgress
    {
        get
        {
            if (CurrentSample is null) return 0;
            var trackLength = _liveContext?.TrackLengthMeters > 0
                ? _liveContext.TrackLengthMeters
                : Math.Max(1, RealtimeTrackPoints.Count > 0 ? RealtimeTrackPoints.Max(point => point.DistanceMeters) : 1);
            return Math.Clamp(CurrentSample.LapDistanceMeters / trackLength, 0, 1);
        }
    }
    public IReadOnlyList<LapRecord> Laps => CurrentSession?.Laps ?? [];
    public IReadOnlyList<MultiLapRow> MultiLapRows => _multiLapRows;
    public IReadOnlyList<LapTrace> MultiLapTraces => _multiLapTraces;
    public IReadOnlyList<LapHistogramBar> LapHistogramBars => _lapHistogramBars;
    public int SelectedMultiLapNumber => SelectedMultiLapRow?.LapNumber ?? -1;
    public IReadOnlyList<LapRecord> DashboardLaps => (IsRecording ? _liveLaps : Laps)
        .Where(lap => lap.IsComplete)
        .OrderBy(lap => lap.LapNumber)
        .TakeLast(8)
        .ToArray();
    public IReadOnlyList<DrivingEvent> Events => CurrentSession?.Events ?? [];
    public IReadOnlyList<Recommendation> Recommendations => CurrentSession?.Recommendations ?? [];
    public Recommendation? PrimaryRecommendation => Recommendations.FirstOrDefault();
    public Recommendation? PrimarySetupRecommendation => Recommendations.FirstOrDefault(item => item.Type == "Setup");
    public string RecommendationHeadline => PrimaryRecommendation?.Title ?? "数据不足";
    public string RecommendationCountText => $"{Recommendations.Count} 项";
    public string RecommendationImpactText => PrimaryRecommendation?.SuggestedValue is { } value
        ? $"{value:F3} {PrimaryRecommendation.Unit}"
        : "--";
    public string RecommendationScopeText => PrimaryRecommendation?.Scope ?? "等待完整圈";
    public string RecommendationEvidenceText => PrimaryRecommendation is null
        ? "需要至少 3 个相近条件下的完整有效圈"
        : string.Join("\n", PrimaryRecommendation.Evidence);
    public string RecommendationValidationText => PrimaryRecommendation is null
        ? "数据不足时不会生成预测或调教建议。"
        : $"置信度 {PrimaryRecommendation.Confidence:P0} · {string.Join("；", PrimaryRecommendation.ValidationSteps)}";
    public string SetupRecommendationHeadline => PrimarySetupRecommendation?.Title ?? "等待至少 3 个完整有效圈";
    public string SetupRecommendationEvidenceText => PrimarySetupRecommendation is null
        ? "完成稳定的有效圈后，将根据重复出现的 ABS、TC 和车辆控制信号给出单变量验证建议。"
        : string.Join("\n", PrimarySetupRecommendation.Evidence);
    public string SetupRecommendationValidationText => PrimarySetupRecommendation is null
        ? "不会在证据不足时猜测调教参数。"
        : $"置信度 {PrimarySetupRecommendation.Confidence:P0} · {string.Join("；", PrimarySetupRecommendation.ValidationSteps)}";
    public string ComparisonCurrentLabel => LapComparison.CurrentLap is { } lap ? $"当前：圈 {lap.LapNumber} · {lap.LapTimeSeconds:F3}s" : "当前：不可用";
    public string ComparisonReferenceLabel => LapComparison.ReferenceLap is { } lap ? $"参考：圈 {lap.LapNumber} · {lap.LapTimeSeconds:F3}s" : "参考：不可用";
    public IReadOnlyList<LapRecord> ComparisonLaps => CurrentSession?.Laps.Where(lap => lap.IsComplete).OrderBy(lap => lap.LapNumber).ToArray() ?? [];
    public IReadOnlyList<TelemetrySample> ComparisonCurrentSamples => SamplesForComparisonLap(SelectedComparisonCurrentLap);
    public IReadOnlyList<TelemetrySample> ComparisonReferenceSamples => SamplesForComparisonLap(SelectedComparisonReferenceLap);
    public string LapDeltaText => LapComparison.IsAvailable ? $"{LapComparison.DeltaSeconds:+0.000;-0.000;0.000} s" : "--";
    public string Sector1DeltaText => LapComparison.Sectors.ElementAtOrDefault(0)?.DeltaText ?? "--";
    public string Sector2DeltaText => LapComparison.Sectors.ElementAtOrDefault(1)?.DeltaText ?? "--";
    public string Sector3DeltaText => LapComparison.Sectors.ElementAtOrDefault(2)?.DeltaText ?? "--";
    public string Sector1CurrentText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(0)?.CurrentSeconds);
    public string Sector1ReferenceText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(0)?.ReferenceSeconds);
    public string Sector2CurrentText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(1)?.CurrentSeconds);
    public string Sector2ReferenceText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(1)?.ReferenceSeconds);
    public string Sector3CurrentText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(2)?.CurrentSeconds);
    public string Sector3ReferenceText => FormatSeconds(LapComparison.Sectors.ElementAtOrDefault(2)?.ReferenceSeconds);
    public string ComparisonMaxSpeedText
    {
        get
        {
            var maximum = ComparisonCurrentSamples.Concat(ComparisonReferenceSamples)
                .Select(sample => sample.SpeedMetersPerSecond * 3.6).DefaultIfEmpty(0).Max();
            return $"最大速度：{maximum:F0} km/h";
        }
    }
    public string BrakePointDifferenceText => LapComparison.IsAvailable ? FormatDistanceDelta(FirstBrakeDistance(ComparisonCurrentSamples) - FirstBrakeDistance(ComparisonReferenceSamples)) : "--";
    public string BrakePointDifferenceHint => LapComparison.IsAvailable ? DirectionHint(FirstBrakeDistance(ComparisonCurrentSamples) - FirstBrakeDistance(ComparisonReferenceSamples), "更晚", "更早") : "等待选择";
    public string FullThrottleDifferenceText => LapComparison.IsAvailable ? $"{FullThrottleRatio(ComparisonCurrentSamples) - FullThrottleRatio(ComparisonReferenceSamples):+0.0%;-0.0%;0.0%}" : "--";
    public string FullThrottleDifferenceHint => LapComparison.IsAvailable ? DirectionHint(FullThrottleRatio(ComparisonCurrentSamples) - FullThrottleRatio(ComparisonReferenceSamples), "更多", "更少") : "等待选择";
    public string MinimumSpeedDifferenceText => LapComparison.IsAvailable ? FormatSpeedDelta(MinimumSpeed(ComparisonCurrentSamples) - MinimumSpeed(ComparisonReferenceSamples)) : "--";
    public string MinimumSpeedDifferenceHint => LapComparison.IsAvailable ? DirectionHint(MinimumSpeed(ComparisonCurrentSamples) - MinimumSpeed(ComparisonReferenceSamples), "更快", "更慢") : "等待选择";
    public string ExitSpeedDifferenceText => LapComparison.IsAvailable ? FormatSpeedDelta(ExitSpeed(ComparisonCurrentSamples) - ExitSpeed(ComparisonReferenceSamples)) : "--";
    public string ExitSpeedDifferenceHint => LapComparison.IsAvailable ? DirectionHint(ExitSpeed(ComparisonCurrentSamples) - ExitSpeed(ComparisonReferenceSamples), "更快", "更慢") : "等待选择";
    public IReadOnlyList<TrackPoint> ComparisonCurrentPoints => LapComparison.CurrentPoints;
    public IReadOnlyList<TrackPoint> ComparisonReferencePoints => LapComparison.ReferencePoints;
    public IReadOnlyList<TrackPoint> SelectedCornerTrackPoints => SelectedCorner?.TrackPoints ?? [];
    public string SelectedCornerTitle => SelectedCorner is null ? "尚无可分析弯道" : $"{SelectedCorner.Name} · {SelectedCorner.DistanceText}";
    public string SelectedCornerLossText => SelectedCorner?.DeltaText ?? "--";
    public string SelectedCornerSpeedText => SelectedCorner?.SpeedText ?? "等待完整圈";
    public bool IsConnectionSettingsVisible => SettingsSection == "Connection";
    public bool IsDisplaySettingsVisible => SettingsSection == "Display";
    public bool IsStorageSettingsVisible => SettingsSection == "Storage";
    public bool IsAboutSettingsVisible => SettingsSection == "About";
    public bool IsChineseLanguage
    {
        get => LocalizationManager.CurrentLanguage == LocalizationManager.ChineseLanguage;
        set { if (value) SetLanguage(LocalizationManager.ChineseLanguage); }
    }
    public bool IsEnglishLanguage
    {
        get => LocalizationManager.CurrentLanguage == LocalizationManager.EnglishLanguage;
        set { if (value) SetLanguage(LocalizationManager.EnglishLanguage); }
    }
    public bool IsGermanLanguage
    {
        get => LocalizationManager.CurrentLanguage == LocalizationManager.GermanLanguage;
        set { if (value) SetLanguage(LocalizationManager.GermanLanguage); }
    }
    public IReadOnlyList<ILanguagePack> AvailableLanguages => LocalizationManager.AvailableLanguagePacks;
    public string SelectedLanguage
    {
        get => LocalizationManager.CurrentLanguage;
        set => SetLanguage(value);
    }
    public bool IsClassicPedalDisplay
    {
        get => !IsAxisPedalDisplay;
        set
        {
            if (value) IsAxisPedalDisplay = false;
        }
    }
    public IReadOnlyList<TelemetrySample> PedalTraceSamples => IsRecording ? _livePedalTraceSnapshot : ReplaySamples;
    public string FilteredStoredSessionCountText => $"共 {FilteredStoredSessions.Count} 个会话";
    public IReadOnlyList<string> LibraryTimeFilters { get; } = ["全部时间", "最近 7 天", "最近 30 天"];
    public IReadOnlyList<string> LibraryValidityFilters { get; } = ["全部", "有有效圈", "完整记录"];
    public string StoragePath => _repository.RootDirectory;
    public int StoredSessionCount => _repository.GetStorageStatistics().Count;
    public string StorageSizeText => FormatBytes(_repository.GetStorageStatistics().Bytes);
    public string AppVersionText => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    public double RealtimeTrackPanelRatio => Math.Clamp(_preferences.RealtimeTrackPanelRatio, 0.28, 0.72);
    public double ReplayTrackPanelRatio => Math.Clamp(_preferences.ReplayTrackPanelRatio, 0.28, 0.72);
    public int SampleCount => IsRecording ? _liveSamples.Count : CurrentSession?.Samples.Count ?? 0;
    private IReadOnlyList<LapRecord> OverviewLaps => IsRecording ? _liveLaps : CurrentSession?.Laps ?? [];
    public int CompleteLapCount => OverviewLaps.Count(lap => lap.IsComplete);
    public int ValidCompleteLapCount => OverviewLaps.Count(lap => lap.IsComplete && lap.IsValid);
    public int TotalOverviewLapCount => OverviewLaps.Count;
    public string BestLapTimeText => OverviewLaps.Where(lap => lap.IsComplete && lap.IsValid)
        .OrderBy(lap => lap.LapTimeSeconds).FirstOrDefault() is { } best ? FormatTime(best.LapTimeSeconds) : "--:--.---";
    public string AverageLapTimeText
    {
        get
        {
            var laps = OverviewLaps.Where(lap => lap.IsComplete && lap.IsValid).ToArray();
            return laps.Length == 0 ? "--:--.---" : FormatTime(laps.Average(lap => lap.LapTimeSeconds));
        }
    }
    public string TheoreticalBestLapText
    {
        get
        {
            var valid = MultiLapRows.Where(row => row.IsComplete && row.IsValid).ToArray();
            if (valid.Length == 0) return "--:--.---";
            var bestSectors = new[]
            {
                valid.Where(row => row.Sector1Seconds > 0).Select(row => row.Sector1Seconds).DefaultIfEmpty().Min(),
                valid.Where(row => row.Sector2Seconds > 0).Select(row => row.Sector2Seconds).DefaultIfEmpty().Min(),
                valid.Where(row => row.Sector3Seconds > 0).Select(row => row.Sector3Seconds).DefaultIfEmpty().Min()
            };
            if (!bestSectors.All(value => value > 0)) return "--:--.---";
            return FormatTime(Math.Min(bestSectors.Sum(), valid.Min(row => row.LapTimeSeconds)));
        }
    }
    public string ValidLapRatioText => OverviewLaps.Count == 0 ? "0%" : $"{(double)ValidCompleteLapCount / OverviewLaps.Count:P0}";
    public string AllLapCountText => $"全部圈  {OverviewLaps.Count}";
    public string ValidLapCountText => $"有效圈  {ValidCompleteLapCount}";
    public string InvalidLapCountText => $"无效/片段  {OverviewLaps.Count - ValidCompleteLapCount}";
    public string LapSpreadText
    {
        get
        {
            var times = OverviewLaps.Where(lap => lap.IsComplete && lap.IsValid).Select(lap => lap.LapTimeSeconds).ToArray();
            return times.Length < 2 ? "需要至少 2 个有效圈" : $"最快与最慢相差 {times.Max() - times.Min():F3} s";
        }
    }
    public string TrackQualityText => CompleteLapCount > 0 ? "单圈实测" : "片段";
    public string ReplayLapText => CurrentSample is null ? "等待遥测" : SelectedReplayLap is { } lap
        ? $"圈 {lap.LapNumber} · {FormatTime(CurrentSample.SessionElapsedSeconds - lap.StartedAtSeconds)} / {FormatTime(lap.LapTimeSeconds)}"
        : $"圈 {CurrentSample.LapNumber} · {FormatTime(CurrentSample.SessionElapsedSeconds)}";
    public string ReplayAvailabilityText => CompleteLapCount > 0 ? $"{CompleteLapCount} 个完整圈可回放" : "仅可回放采集片段";
    public IReadOnlyList<LapRecord> ReplayableLaps => CurrentSession?.Laps.Where(lap => lap.IsComplete).ToArray() ?? [];
    public IReadOnlyList<TelemetrySample> ReplaySamples => _replaySamples;
    public IReadOnlyList<TrackPoint> ReplayTrackPoints => _replayTrackPoints;
    public IReadOnlyList<DrivingEvent> ReplayEvents => _replayEvents;
    public IReadOnlyList<ReplayEventRow> ReplayEventRows => _replayEvents.Select((item, index) => new ReplayEventRow(
        FormatTime(Math.Max(0, item.SessionElapsedSeconds - (SelectedReplayLap?.StartedAtSeconds ?? 0))),
        EventDisplayName(item.Type),
        $"T{index + 1}",
        EventValueText(item),
        EventAccent(item.Type))).ToArray();
    public string DurationText
    {
        get
        {
            if (IsRecording && _liveSamples.TryPeek(out var first) && CurrentSample is { } current)
            {
                return FormatTime((current.CapturedAtUtc - first.CapturedAtUtc).TotalSeconds);
            }
            return FormatTime(CurrentSession?.DurationSeconds ?? 0);
        }
    }
    public string SpeedText => $"{(CurrentSample?.SpeedMetersPerSecond ?? 0) * 3.6:F0}";
    public double SpeedValue => (CurrentSample?.SpeedMetersPerSecond ?? 0) * 3.6;
    public string GearText => CurrentSample?.Gear switch { -1 => "R", 0 => "N", int gear => gear.ToString(), _ => "--" };
    public string RpmText => $"{CurrentSample?.EngineRpm ?? 0:F0}";
    public double RpmValue => CurrentSample?.EngineRpm ?? 0;
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
    public string SteeringText => CurrentSample is null ? "--°" : $"{SteeringDegrees:+0;-0;0}°";
    public double SteeringDegrees
    {
        get
        {
            if (CurrentSample is not { } sample) return 0;
            var fullRange = double.IsFinite(sample.SteeringWheelRangeDegrees) && sample.SteeringWheelRangeDegrees >= 180
                ? sample.SteeringWheelRangeDegrees
                : 1080;
            return Math.Clamp(sample.Steering, -1, 1) * fullRange / 2;
        }
    }
    public double LateralGValue => (CurrentSample?.LocalAcceleration.X ?? 0) / 9.80665;
    public double LongitudinalGValue => (CurrentSample?.LocalAcceleration.Z ?? 0) / 9.80665;
    public string LateralGText => $"{LateralGValue:+0.00;-0.00;0.00} G";
    public string LongitudinalGText => $"{LongitudinalGValue:+0.00;-0.00;0.00} G";
    public string LapTimeText
    {
        get
        {
            if (CurrentSample is null) return "--:--.---";
            var start = IsRecording
                ? _currentLapStartedAtSeconds
                : SelectedReplayLap?.StartedAtSeconds ?? CurrentSample.SessionElapsedSeconds;
            return FormatTime(Math.Max(0, CurrentSample.SessionElapsedSeconds - start));
        }
    }
    public string BestDeltaText
    {
        get
        {
            var laps = IsRecording ? _liveLaps : CurrentSession?.Laps ?? [];
            var valid = laps.Where(lap => lap.IsComplete && lap.IsValid).ToArray();
            if (valid.Length == 0) return "--";
            var displayed = IsRecording ? valid.LastOrDefault() : SelectedReplayLap;
            if (displayed is null || !displayed.IsComplete) return "--";
            var delta = displayed.LapTimeSeconds - valid.Min(lap => lap.LapTimeSeconds);
            return $"{delta:+0.000;-0.000;0.000}";
        }
    }
    public string AbsLevelText => CurrentSample is null ? "--" : CurrentSample.Controls.Abs.ToString();
    public string TcLevelText => CurrentSample is null ? "--" : CurrentSample.Controls.TractionControl.ToString();
    public string AbsStatusText => CurrentSample is null ? "NO DATA" : CurrentSample.AbsActive ? "ACTIVE" : "READY";
    public string TcStatusText => CurrentSample is null ? "NO DATA" : CurrentSample.TcActive ? "ACTIVE" : "READY";
    public bool IsErsAvailable => CurrentSample is { } sample
        && double.IsFinite(sample.ErsBatteryFraction)
        && sample.ErsBatteryFraction >= 0
        && (sample.ElectricMotorState > 0 || sample.ErsBatteryFraction > 0);
    public string ErsBatteryText => IsErsAvailable ? $"{Math.Clamp(CurrentSample!.ErsBatteryFraction, 0, 1):P0}" : "--";
    public string ErsStateText => CurrentSample?.ElectricMotorState switch
    {
        2 => "DEPLOY",
        3 => "REGEN",
        1 => "READY",
        0 when IsErsAvailable => "STANDBY",
        _ => "NO DATA"
    };
    public string BrakeBiasText => CurrentSample is null ? "--" : $"{1 - Math.Clamp(CurrentSample.BrakeBiasRear, 0, 1):P1}";
    public string BrakeBiasDetailText => CurrentSample is null
        ? "前 / 后"
        : $"前 {1 - Math.Clamp(CurrentSample.BrakeBiasRear, 0, 1):P0} · 后 {Math.Clamp(CurrentSample.BrakeBiasRear, 0, 1):P0}";
    public string FrontLeftTireTempText => TireTemperatureText(0);
    public string FrontRightTireTempText => TireTemperatureText(1);
    public string RearLeftTireTempText => TireTemperatureText(2);
    public string RearRightTireTempText => TireTemperatureText(3);
    public string FrontLeftBrakeTempText => BrakeTemperatureText(0);
    public string FrontRightBrakeTempText => BrakeTemperatureText(1);
    public string RearLeftBrakeTempText => BrakeTemperatureText(2);
    public string RearRightBrakeTempText => BrakeTemperatureText(3);
    public string FrontLeftTireWearText => TireWearText(0);
    public string FrontRightTireWearText => TireWearText(1);
    public string RearLeftTireWearText => TireWearText(2);
    public string RearRightTireWearText => TireWearText(3);
    public string FuelText => $"{CurrentSample?.FuelLiters ?? 0:F1} L";
    public string FuelRemainingLapsText => CurrentSample is not null && _fuelPerLap > 0.01
        ? $"{CurrentSample.FuelLiters / _fuelPerLap:F0} 圈"
        : "-- 圈";
    public string LapDistanceText => $"{CurrentSample?.LapDistanceMeters ?? 0:F1} m";
    public string CurrentLapText => CurrentSample is null ? "--" : CurrentSample.LapNumber.ToString();
    public string CurrentTireText => ResolveTireText(CurrentSample);
    public string CurrentTireAccent => ResolveTireAccent(CurrentSample);
    public string CurrentWeatherText => WeatherText(CurrentSample?.Environment, CurrentSession?.Metadata.WeatherConditions);
    public string WeatherIconText => WeatherIcon(CurrentSample?.Environment, CurrentSession?.Metadata.WeatherConditions);
    public string WeatherPrecipitationText => CurrentSample?.Environment.RainFraction is { } rain && double.IsFinite(rain) && rain >= 0
        ? $"降水概率 {Math.Clamp(rain, 0, 1):P0}"
        : "降水概率 --";
    public string WeatherDetailText => $"{WeatherPrecipitationText} · {AmbientText}";
    public string AmbientText => CurrentSample is null ? "--°C" : $"{CurrentSample.Environment.AmbientTemperatureCelsius:F1}°C";
    public string TrackTemperatureText => $"{CurrentSample?.Environment.TrackTemperatureCelsius ?? 0:F1}°C";
    public string PlaybackRateText => $"{PlaybackRate:0.##}x";
    public string ReplayElapsedText => CurrentSample is null || ReplaySamples.Count == 0
        ? "--:--.---"
        : FormatTime(CurrentSample.SessionElapsedSeconds - ReplaySamples[0].SessionElapsedSeconds);
    public string ReplayTotalText => ReplaySamples.Count < 2
        ? "--:--.---"
        : FormatTime(ReplaySamples[^1].SessionElapsedSeconds - ReplaySamples[0].SessionElapsedSeconds);
    public double ReplayStartSeconds => ReplaySamples.Count > 0 ? ReplaySamples[0].SessionElapsedSeconds : 0;
    public double ReplayDurationSeconds => ReplaySamples.Count > 1 ? Math.Max(0, ReplaySamples[^1].SessionElapsedSeconds - ReplaySamples[0].SessionElapsedSeconds) : 0;
    public string Sector1TimeText => SectorTimeText(0);
    public string Sector2TimeText => SectorTimeText(1);
    public string Sector3TimeText => SectorTimeText(2);
    public string CompletenessText => CurrentSession?.Metadata.CompletenessDiagnostic ?? "等待导入或实时采集";
    public string ImportSummary => CurrentSession is null ? "尚无遥测" : $"{SampleCount:N0} 样本 · {DurationText} · {CurrentSession.Track.CenterLine.Count:N0} 轨迹点";
    public string SamplingRateText => IsRecording || CurrentSession?.Metadata.DataSource == TelemetryDataSource.LmuSharedMemory
        ? "50 Hz 实时"
        : CurrentSession?.Metadata.DataSource == TelemetryDataSource.LmuNativeDuckDb
            ? "100 Hz 文件"
            : "--";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            Connection = _probe.Probe(Connection.InstallationPath);
            StatusMessage = Connection.Diagnostic;
            if (!Connection.SharedMemoryAvailable)
            {
                await ImportLatestAsync();
            }
            await RefreshLibraryAsync();
            var latestSaved = await _repository.OpenLatestAsync();
            if (latestSaved is not null)
            {
                LoadSession(latestSaved);
                StatusMessage = $"已自动打开最近记录：{latestSaved.Metadata.TrackName}，{latestSaved.Laps.Count(lap => lap.IsComplete)} 个完整圈。";
            }
            RefreshRecoverableSessions();
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
            LoadSession(session);
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

    private async Task RecoverLatestAsync()
    {
        var directory = SessionRecorder.FindRecoverableSessions(TempRoot)
            .FirstOrDefault(path => !string.Equals(path, _recorder?.SessionDirectory, StringComparison.OrdinalIgnoreCase));
        if (directory is null)
        {
            RefreshRecoverableSessions();
            StatusMessage = "没有可恢复的临时记录。";
            return;
        }

        IsBusy = true;
        StatusMessage = "正在恢复中断的临时记录…";
        try
        {
            var recovered = await SessionRecorder.RecoverAsync(directory);
            var samples = recovered.Samples;
            var laps = TelemetryLapBuilder.Build(samples);
            var completeLaps = laps.Count(lap => lap.IsComplete);
            var validLaps = laps.Count(lap => lap.IsComplete && lap.IsValid);
            var diagnostic = $"{recovered.Metadata.CompletenessDiagnostic} 识别 {completeLaps} 个完整圈、{validLaps} 个有效圈。";
            var metadata = recovered.Metadata with { IsComplete = completeLaps > 0, CompletenessDiagnostic = diagnostic };
            var track = GpsTrackReconstructor.FromSamples(metadata.TrackName, samples, completeLaps > 0);
            var events = _eventDetector.Detect(samples);
            var session = new TelemetrySession(1, metadata, samples, laps, track, events, []);
            LoadSession(session with { Recommendations = _recommendationEngine.Analyze(session) });
            StatusMessage = $"已恢复：{diagnostic}";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Unable to recover temporary recording from {Directory}", directory);
            StatusMessage = $"恢复临时记录失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshAll();
        }
    }

    private async Task StartRecordingAsync()
    {
        if (!CanStartRecording) return;
        IsBusy = true;
        LmuSharedMemoryReader? reader = null;
        try
        {
            reader = new LmuSharedMemoryReader(Connection);
            if (!reader.TryReadConsistentSnapshot(out var initialSample, out _) || initialSample is null || reader.CurrentContext is null)
            {
                StatusMessage = "LMU_Data 已打开，但尚未解析到玩家车辆。请进入赛道并取得车辆控制后重试。";
                await reader.DisposeAsync();
                IsBusy = false;
                return;
            }

            while (_liveSamples.TryDequeue(out _)) { }
            _liveTrackPoints.Clear();
            _liveTrackPointsSnapshot = [];
            _livePedalTraceSamples.Clear();
            _livePedalTraceSamples.Add(initialSample);
            _livePedalTraceSnapshot = _livePedalTraceSamples.ToArray();
            _completedLiveTrackPoints = [];
            _liveLaps = [];
            _multiLapRows = [];
            _multiLapTraces = [];
            _lapHistogramBars = [];
            SelectedMultiLapRow = null;
            Array.Clear(_liveSectorTimes);
            _fuelPerLap = 0;
            _liveTrackLapNumber = initialSample.LapNumber;
            _captureException = null;
            _liveContext = reader.CurrentContext;
            _currentLapStartedAtSeconds = _liveContext.LapStartElapsedSeconds > 0
                ? _liveContext.LapStartElapsedSeconds
                : initialSample.SessionElapsedSeconds;
            _lastLiveSampleAtUtc = DateTimeOffset.UtcNow;
            CurrentSample = initialSample;
            _recordingCancellation = new CancellationTokenSource();
            _recorder = new SessionRecorder();
            var metadata = CreateLiveMetadata(_liveContext, initialSample.CapturedAtUtc, initialSample.CapturedAtUtc,
                false, "实时记录尚未结束");
            CurrentSession = new TelemetrySession(1, metadata, [], [],
                new TrackDefinition(1, metadata.TrackName, "runtime", TrackGeometrySource.RuntimeReconstructionPartial,
                    null, string.Empty, [], "正在从 LMU_Data 重建赛道轨迹。"), [], []);
            await _recorder.StartAsync(TempRoot, metadata, _recordingCancellation.Token);
            IsRecording = true;
            CurrentPage = PageKind.Realtime;
            StatusMessage = $"正在通过只读 LMU_Data 采集：{metadata.TrackName} / {metadata.VehicleName}";

            var captureReader = reader;
            reader = null;
            _captureTask = Task.Run(async () =>
            {
                long receivedCount = 0;
                try
                {
                    await using (captureReader)
                    await foreach (var sample in captureReader.ReadAllAsync(_recordingCancellation.Token))
                    {
                        receivedCount++;
                        _liveSamples.Enqueue(sample);
                        _liveContext = captureReader.CurrentContext ?? _liveContext;
                        _lastLiveSampleAtUtc = DateTimeOffset.UtcNow;
                        await _recorder.EnqueueAsync(sample, _recordingCancellation.Token);
                        // Keep capture at the source rate, but commit one coherent UI frame at 10 Hz.
                        // Scheduling below Render priority prevents telemetry updates from interrupting
                        // an in-progress WPF/Skia paint, which otherwise appears as panel flicker.
                        if (receivedCount % 5 == 0)
                        {
                            await App.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (_liveTrackLapNumber != sample.LapNumber)
                                {
                                    CompleteLiveLap(sample);
                                }
                                if (_livePedalTraceSamples.Count > 0 && _livePedalTraceSamples[^1].LapNumber != sample.LapNumber)
                                {
                                    _livePedalTraceSamples.Clear();
                                }
                                _livePedalTraceSamples.Add(sample);
                                if (_livePedalTraceSamples.Count > 500)
                                {
                                    _livePedalTraceSamples.RemoveRange(0, _livePedalTraceSamples.Count - 500);
                                }
                                _livePedalTraceSnapshot = _livePedalTraceSamples.ToArray();
                                CurrentSample = sample;
                                if (receivedCount % 10 == 0 && sample.Quality.IsWorldPositionValid && sample.LapDistanceMeters >= 0)
                                {
                                    _liveTrackPoints.Add(new TrackPoint(sample.LapDistanceMeters,
                                        sample.WorldPosition.X, -sample.WorldPosition.Z, sample.WorldPosition.Y));
                                    _liveTrackPointsSnapshot = _liveTrackPoints.ToArray();
                                    OnPropertyChanged(nameof(TrackPoints));
                                    OnPropertyChanged(nameof(RealtimeTrackPoints));
                                }
                                OnPropertyChanged(nameof(PedalTraceSamples));
                                OnPropertyChanged(nameof(SampleCount));
                                OnPropertyChanged(nameof(DurationText));
                                OnPropertyChanged(nameof(ImportSummary));
                                if (StatusMessage.StartsWith("LMU_Data 已连接", StringComparison.Ordinal))
                                {
                                    StatusMessage = $"正在通过只读 LMU_Data 采集：{_liveContext?.TrackName} / {_liveContext?.VehicleName}";
                                }
                            }, DispatcherPriority.Background);
                        }
                    }
                }
                catch (OperationCanceledException) when (_recordingCancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    _captureException = exception;
                    Log.Error(exception, "Live LMU capture failed");
                    await App.Current.Dispatcher.InvokeAsync(() =>
                        StatusMessage = $"实时采集发生错误：{exception.Message}。请结束记录以保存已采集数据。");
                }
            });
            IsBusy = false;
            RefreshAll();
        }
        catch (Exception exception)
        {
            if (reader is not null) await reader.DisposeAsync();
            Log.Error(exception, "Unable to start live LMU capture");
            StatusMessage = $"无法开始实时采集：{exception.Message}";
            IsRecording = false;
            IsBusy = false;
            RefreshAll();
        }
    }

    private async Task EndRecordingAsync()
    {
        if (!IsRecording) return;
        IsBusy = true;
        IsRecording = false;
        try
        {
            _recordingCancellation?.Cancel();
            if (_captureTask is not null) await _captureTask;
            if (_recorder is not null)
            {
                await _recorder.FinishAsync();
                await _recorder.DisposeAsync();
            }
            var samples = _liveSamples.ToArray();
            if (samples.Length > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var laps = TelemetryLapBuilder.Build(samples);
                var completeLaps = laps.Count(lap => lap.IsComplete);
                var validLaps = laps.Count(lap => lap.IsComplete && lap.IsValid);
                var observedRate = CalculateObservedRate(samples);
                var missingRate = CalculateMissingRate(samples, 50);
                var diagnostic = $"实时记录完成：{samples.Length:N0} 个样本，平均 {observedRate:F1} Hz，估算缺失率 {missingRate:P3}；{completeLaps} 个完整圈，其中 {validLaps} 个有效；首尾采集片段保守标记为不完整。";
                if (_captureException is not null) diagnostic += $" 采集提前结束：{_captureException.Message}";
                var metadata = CreateLiveMetadata(_liveContext, samples[0].CapturedAtUtc, now, completeLaps > 0, diagnostic,
                    CurrentSession?.Metadata.SessionId);
                var track = GpsTrackReconstructor.FromSamples(metadata.TrackName, samples, completeLaps > 0);
                var detectedEvents = _eventDetector.Detect(samples);
                var session = new TelemetrySession(1, metadata, samples, laps, track, detectedEvents, []);
                LoadSession(session with { Recommendations = _recommendationEngine.Analyze(session) });
                if (CurrentSession is not null)
                {
                    LastSavedPath = await _repository.SaveAsync(CurrentSession);
                    await RefreshLibraryAsync(LastSavedPath);
                }
                StatusMessage = $"{diagnostic} 已生成 {CurrentSession?.Recommendations.Count ?? 0} 条有证据建议，并自动保存到记录库。";
            }
            else StatusMessage = "记录结束，但未收到新的 LMU 遥测样本。";
            IsEndModalVisible = true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Unable to finalize live LMU capture");
            StatusMessage = $"结束记录时发生错误：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshAll();
        }
    }

    private void ToggleReplay()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
        {
            _lastReplayTickTimestamp = Stopwatch.GetTimestamp();
            _replayTimer.Start();
        }
        else
        {
            _replayTimer.Stop();
            _lastReplayTickTimestamp = 0;
        }
    }

    private void StepReplay(int direction)
    {
        if (ReplaySamples.Count <= 1) return;
        ReplayProgress = Math.Clamp(ReplayProgress + direction / (double)(ReplaySamples.Count - 1), 0, 1);
    }

    private void SeekReplay(double seconds)
    {
        if (ReplaySamples.Count <= 1) return;
        var duration = ReplaySamples[^1].SessionElapsedSeconds - ReplaySamples[0].SessionElapsedSeconds;
        if (duration <= 0) return;
        ReplayProgress = Math.Clamp(ReplayProgress + seconds / duration, 0, 1);
    }

    private void JumpToEvent(int direction)
    {
        if (ReplaySamples.Count <= 1 || ReplayEvents.Count == 0 || direction == 0) return;
        var start = ReplaySamples[0].SessionElapsedSeconds;
        var duration = ReplaySamples[^1].SessionElapsedSeconds - start;
        if (duration <= 0) return;
        var currentTime = start + ReplayProgress * duration;
        var target = direction < 0
            ? ReplayEvents.LastOrDefault(item => item.SessionElapsedSeconds < currentTime - 0.01)
            : ReplayEvents.FirstOrDefault(item => item.SessionElapsedSeconds > currentTime + 0.01);
        if (target is null) return;
        ReplayProgress = Math.Clamp((target.SessionElapsedSeconds - start) / duration, 0, 1);
    }

    private void ReplayTick(object? sender, EventArgs e)
    {
        if (!IsPlaying || ReplaySamples.Count <= 1) return;

        var now = Stopwatch.GetTimestamp();
        if (_lastReplayTickTimestamp == 0)
        {
            _lastReplayTickTimestamp = now;
            return;
        }

        var elapsedSeconds = (now - _lastReplayTickTimestamp) / (double)Stopwatch.Frequency;
        _lastReplayTickTimestamp = now;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return;

        // Keep playback smooth after a debugger break or a temporarily blocked UI thread.
        elapsedSeconds = Math.Min(elapsedSeconds, 0.25);
        var duration = ReplaySamples[^1].SessionElapsedSeconds - ReplaySamples[0].SessionElapsedSeconds;
        if (!double.IsFinite(duration) || duration <= 0) return;

        var next = ReplayProgress + elapsedSeconds * Math.Max(0, PlaybackRate) / duration;
        ReplayProgress = next >= 1 ? next - Math.Floor(next) : Math.Max(0, next);
    }

    partial void OnReplayProgressChanged(double value)
    {
        if (ReplaySamples.Count > 0)
        {
            var index = Math.Clamp((int)Math.Round(value * (ReplaySamples.Count - 1)), 0, ReplaySamples.Count - 1);
            CurrentSample = ReplaySamples[index];
        }
    }

    partial void OnCurrentSampleChanged(TelemetrySample? value) => RefreshTelemetry();
    partial void OnCurrentSessionChanged(TelemetrySession? value) => RefreshAll();
    partial void OnConnectionChanged(LmuConnectionStatus value) => RefreshAll();
    partial void OnIsRecordingChanged(bool value) => RefreshAll();
    partial void OnIsAxisPedalDisplayChanged(bool value)
    {
        OnPropertyChanged(nameof(IsClassicPedalDisplay));
        _preferences = _preferences with { UseAxisPedalDisplay = value };
        SavePreferences();
    }
    partial void OnIsBusyChanged(bool value) => RefreshCommands();
    partial void OnRecoverableSessionCountChanged(int value) => RecoverLatestCommand.NotifyCanExecuteChanged();
    partial void OnSelectedCornerChanged(CornerAnalysisResult? value)
    {
        OnPropertyChanged(nameof(SelectedCornerTrackPoints));
        OnPropertyChanged(nameof(SelectedCornerTitle));
        OnPropertyChanged(nameof(SelectedCornerLossText));
        OnPropertyChanged(nameof(SelectedCornerSpeedText));
    }
    partial void OnSelectedReplayLapChanged(LapRecord? value) => PrepareReplay(value);
    partial void OnSelectedMultiLapRowChanged(MultiLapRow? value) => OnPropertyChanged(nameof(SelectedMultiLapNumber));
    partial void OnSelectedComparisonCurrentLapChanged(LapRecord? value) => RefreshSelectedComparison();
    partial void OnSelectedComparisonReferenceLapChanged(LapRecord? value) => RefreshSelectedComparison();
    partial void OnLibrarySearchTextChanged(string value) => ApplyLibraryFilters();
    partial void OnSelectedLibraryTrackFilterChanged(string value) => ApplyLibraryFilters();
    partial void OnSelectedLibraryVehicleFilterChanged(string value) => ApplyLibraryFilters();
    partial void OnSelectedLibraryTimeFilterChanged(string value) => ApplyLibraryFilters();
    partial void OnSelectedLibrarySessionFilterChanged(string value) => ApplyLibraryFilters();
    partial void OnSelectedLibraryValidityFilterChanged(string value) => ApplyLibraryFilters();
    partial void OnFilteredStoredSessionsChanged(ObservableCollection<StoredSessionInfo> value) => OnPropertyChanged(nameof(FilteredStoredSessionCountText));
    partial void OnPlaybackRateChanged(double value)
    {
        OnPropertyChanged(nameof(PlaybackRateText));
    }
    partial void OnSelectedStoredSessionChanged(StoredSessionInfo? value)
    {
        OpenSelectedSessionCommand.NotifyCanExecuteChanged();
        ExportSelectedSessionCommand.NotifyCanExecuteChanged();
        DeleteSelectedSessionCommand.NotifyCanExecuteChanged();
        RefreshLibraryCommand.NotifyCanExecuteChanged();
    }
    partial void OnSettingsSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsConnectionSettingsVisible));
        OnPropertyChanged(nameof(IsDisplaySettingsVisible));
        OnPropertyChanged(nameof(IsStorageSettingsVisible));
        OnPropertyChanged(nameof(IsAboutSettingsVisible));
        OnPropertyChanged(nameof(StoredSessionCount));
        OnPropertyChanged(nameof(StorageSizeText));
    }
    partial void OnLapComparisonChanged(LapComparisonResult value)
    {
        OnPropertyChanged(nameof(ComparisonCurrentLabel));
        OnPropertyChanged(nameof(ComparisonReferenceLabel));
        OnPropertyChanged(nameof(LapDeltaText));
        OnPropertyChanged(nameof(Sector1DeltaText));
        OnPropertyChanged(nameof(Sector2DeltaText));
        OnPropertyChanged(nameof(Sector3DeltaText));
        OnPropertyChanged(nameof(Sector1CurrentText));
        OnPropertyChanged(nameof(Sector1ReferenceText));
        OnPropertyChanged(nameof(Sector2CurrentText));
        OnPropertyChanged(nameof(Sector2ReferenceText));
        OnPropertyChanged(nameof(Sector3CurrentText));
        OnPropertyChanged(nameof(Sector3ReferenceText));
        OnPropertyChanged(nameof(ComparisonCurrentPoints));
        OnPropertyChanged(nameof(ComparisonReferencePoints));
        OnPropertyChanged(nameof(ComparisonCurrentSamples));
        OnPropertyChanged(nameof(ComparisonReferenceSamples));
        OnPropertyChanged(nameof(ComparisonMaxSpeedText));
        OnPropertyChanged(nameof(BrakePointDifferenceText));
        OnPropertyChanged(nameof(BrakePointDifferenceHint));
        OnPropertyChanged(nameof(FullThrottleDifferenceText));
        OnPropertyChanged(nameof(FullThrottleDifferenceHint));
        OnPropertyChanged(nameof(MinimumSpeedDifferenceText));
        OnPropertyChanged(nameof(MinimumSpeedDifferenceHint));
        OnPropertyChanged(nameof(ExitSpeedDifferenceText));
        OnPropertyChanged(nameof(ExitSpeedDifferenceHint));
    }

    public void SaveRealtimeTrackPanelRatio(double ratio)
    {
        _preferences = _preferences with { RealtimeTrackPanelRatio = Math.Clamp(ratio, 0.28, 0.72) };
        OnPropertyChanged(nameof(RealtimeTrackPanelRatio));
        SavePreferences();
    }

    public void SaveReplayTrackPanelRatio(double ratio)
    {
        _preferences = _preferences with { ReplayTrackPanelRatio = Math.Clamp(ratio, 0.28, 0.72) };
        OnPropertyChanged(nameof(ReplayTrackPanelRatio));
        SavePreferences();
    }

    public void SetLmuInstallationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var normalizedPath = Path.GetFullPath(path.Trim());
        _preferences = _preferences with { LmuInstallationPath = normalizedPath };
        SavePreferences();
        Connection = _probe.Probe(normalizedPath);
        StatusMessage = Connection.Diagnostic;
    }

    public void SetLanguage(string language)
    {
        var normalized = LanguagePackRegistry.Resolve(language).LanguageCode;
        if (LocalizationManager.CurrentLanguage == normalized) return;

        _preferences = _preferences with { Language = normalized };
        LocalizationManager.SetLanguage(normalized);
        OnPropertyChanged(nameof(IsChineseLanguage));
        OnPropertyChanged(nameof(IsEnglishLanguage));
        OnPropertyChanged(nameof(IsGermanLanguage));
        OnPropertyChanged(nameof(SelectedLanguage));
        RefreshAll();
        SavePreferences();
    }

    private void SavePreferences()
    {
        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Unable to save display preferences");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Unable to save display preferences");
        }
    }

    private void ConnectionTick(object? sender, EventArgs e)
    {
        if (IsRecording)
        {
            if (_captureException is null && DateTimeOffset.UtcNow - _lastLiveSampleAtUtc > TimeSpan.FromSeconds(2))
            {
                StatusMessage = "LMU_Data 已连接，但遥测暂未推进；请确认游戏未暂停且车辆已进入可驾驶状态。";
            }
            return;
        }
        if (IsBusy) return;
        try
        {
            var updated = _probe.Probe(Connection.InstallationPath);
            if (updated == Connection) return;
            Connection = updated;
            StatusMessage = updated.Diagnostic;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Periodic LMU connection probe failed");
        }
    }

    private void CompleteLiveLap(TelemetrySample nextLapSample)
    {
        if (_liveTrackPoints.Count > 20)
        {
            _completedLiveTrackPoints = _liveTrackPoints.ToArray();
        }
        _liveTrackPoints.Clear();
        _liveTrackPointsSnapshot = [];
        _liveTrackLapNumber = nextLapSample.LapNumber;
        _currentLapStartedAtSeconds = _liveContext?.LapStartElapsedSeconds > 0
            ? _liveContext.LapStartElapsedSeconds
            : nextLapSample.SessionElapsedSeconds;

        var samples = _liveSamples.ToArray();
        _liveLaps = TelemetryLapBuilder.Build(samples);
        var completed = _liveLaps.LastOrDefault(lap => lap.IsComplete);
        if (completed is not null)
        {
            var lapSamples = samples.Where(sample => sample.LapNumber == completed.LapNumber
                && sample.SessionElapsedSeconds >= completed.StartedAtSeconds - 0.01
                && sample.SessionElapsedSeconds <= completed.EndedAtSeconds + 0.01).ToArray();
            var sectors = CalculateSectorTimes(lapSamples);
            for (var index = 0; index < _liveSectorTimes.Length; index++) _liveSectorTimes[index] = sectors[index];
        }
        _fuelPerLap = CalculateFuelPerLap(samples, _liveLaps);
        RefreshMultiLapData();
        OnPropertyChanged(nameof(TrackPoints));
        OnPropertyChanged(nameof(RealtimeTrackPoints));
        OnPropertyChanged(nameof(DashboardLaps));
        OnPropertyChanged(nameof(Sector1TimeText));
        OnPropertyChanged(nameof(Sector2TimeText));
        OnPropertyChanged(nameof(Sector3TimeText));
        OnPropertyChanged(nameof(BestDeltaText));
        OnPropertyChanged(nameof(FuelRemainingLapsText));
    }

    private SessionMetadata CreateLiveMetadata(
        LmuSessionContext? context,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        bool isComplete,
        string diagnostic,
        Guid? sessionId = null) => new(
            1,
            sessionId ?? Guid.NewGuid(),
            string.IsNullOrWhiteSpace(context?.TrackName) ? "Unknown track" : context.TrackName,
            string.IsNullOrWhiteSpace(context?.TrackName) ? "Unknown layout" : context.TrackName,
            string.IsNullOrWhiteSpace(context?.VehicleName) ? "Unknown vehicle" : context.VehicleName,
            context?.VehicleClass ?? string.Empty,
            context?.SessionType ?? "Unknown session",
            startedAtUtc,
            endedAtUtc,
            TelemetryDataSource.LmuSharedMemory,
            "LMU_Data",
            null,
            Connection.HeaderSha256,
            isComplete,
            diagnostic,
            null);

    private void LoadSession(TelemetrySession session)
    {
        var events = session.Events.Count > 0 ? session.Events : _eventDetector.Detect(session.Samples);
        var rebuiltTrack = session.Samples.Count > 1
            ? GpsTrackReconstructor.FromSamples(session.Metadata.TrackName, session.Samples, session.Laps.Any(lap => lap.IsComplete))
            : session.Track;
        var normalized = session with { Events = events, Track = rebuiltTrack };
        normalized = normalized with { Recommendations = _recommendationEngine.Analyze(normalized) };
        CurrentSession = normalized;
        _fuelPerLap = CalculateFuelPerLap(normalized.Samples, normalized.Laps);
        CurrentSample = normalized.Samples.LastOrDefault();
        SetupSnapshot = SetupSnapshotProbe.FromSession(normalized);
        RefreshMultiLapData();
        var comparison = _sessionAnalysisEngine.Compare(normalized);
        _suppressComparisonSelection = true;
        SelectedComparisonCurrentLap = comparison.CurrentLap;
        SelectedComparisonReferenceLap = comparison.ReferenceLap;
        _suppressComparisonSelection = false;
        LapComparison = comparison;
        CornerAnalyses = _sessionAnalysisEngine.AnalyzeCorners(normalized, LapComparison);
        SelectedCorner = CornerAnalyses.FirstOrDefault();
        OnPropertyChanged(nameof(ReplayableLaps));
        OnPropertyChanged(nameof(ComparisonLaps));
        SelectedReplayLap = ReplayableLaps.LastOrDefault();
        PrepareReplay(SelectedReplayLap);
    }

    private void PrepareReplay(LapRecord? lap)
    {
        if (CurrentSession is null)
        {
            _replaySamples = [];
            _replayTrackPoints = [];
            _replayEvents = [];
        }
        else
        {
            _replaySamples = lap is null
                ? CurrentSession.Samples
                : CurrentSession.Samples.Where(sample => sample.LapNumber == lap.LapNumber
                    && sample.SessionElapsedSeconds >= lap.StartedAtSeconds - 0.01
                    && sample.SessionElapsedSeconds <= lap.EndedAtSeconds + 0.01).ToArray();
            _replayTrackPoints = _replaySamples
                .Where(sample => sample.Quality.IsWorldPositionValid && sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0)
                .Where((_, index) => index % 5 == 0)
                .Select(sample => new TrackPoint(sample.LapDistanceMeters, sample.WorldPosition.X, -sample.WorldPosition.Z, sample.WorldPosition.Y))
                .ToArray();
            _replayEvents = lap is null ? CurrentSession.Events : CurrentSession.Events.Where(item => item.LapNumber == lap.LapNumber).ToArray();
        }
        ReplayProgress = 0;
        CurrentSample = _replaySamples.FirstOrDefault();
        OnPropertyChanged(nameof(ReplaySamples));
        OnPropertyChanged(nameof(PedalTraceSamples));
        OnPropertyChanged(nameof(ReplayTrackPoints));
        OnPropertyChanged(nameof(ReplayEvents));
        OnPropertyChanged(nameof(ReplayEventRows));
        OnPropertyChanged(nameof(ReplayLapText));
        OnPropertyChanged(nameof(ReplayElapsedText));
        OnPropertyChanged(nameof(ReplayTotalText));
        OnPropertyChanged(nameof(ReplayStartSeconds));
        OnPropertyChanged(nameof(ReplayDurationSeconds));
        OnPropertyChanged(nameof(Sector1TimeText));
        OnPropertyChanged(nameof(Sector2TimeText));
        OnPropertyChanged(nameof(Sector3TimeText));
        OnPropertyChanged(nameof(BestDeltaText));
        RefreshCommands();
    }

    private void RefreshSelectedComparison()
    {
        if (_suppressComparisonSelection || CurrentSession is null) return;
        LapComparison = _sessionAnalysisEngine.Compare(CurrentSession, SelectedComparisonCurrentLap, SelectedComparisonReferenceLap);
        CornerAnalyses = _sessionAnalysisEngine.AnalyzeCorners(CurrentSession, LapComparison);
        SelectedCorner = CornerAnalyses.FirstOrDefault();
    }

    private void SwapComparisonLaps()
    {
        if (SelectedComparisonCurrentLap is null || SelectedComparisonReferenceLap is null) return;
        var current = SelectedComparisonCurrentLap;
        _suppressComparisonSelection = true;
        SelectedComparisonCurrentLap = SelectedComparisonReferenceLap;
        SelectedComparisonReferenceLap = current;
        _suppressComparisonSelection = false;
        RefreshSelectedComparison();
    }

    private void RefreshMultiLapData()
    {
        var laps = OverviewLaps.OrderBy(lap => lap.LapNumber).ToArray();
        var samples = IsRecording ? _liveSamples.ToArray() : CurrentSession?.Samples.ToArray() ?? [];
        var bestLap = laps.Where(lap => lap.IsComplete && lap.IsValid)
            .OrderBy(lap => lap.LapTimeSeconds).FirstOrDefault();
        var bestTime = bestLap?.LapTimeSeconds;
        var trackLength = ResolveTrackLength(samples);
        var rows = new List<MultiLapRow>(laps.Length);
        var traces = new List<LapTrace>(laps.Length);

        foreach (var lap in laps)
        {
            var lapSamples = samples.Where(sample => sample.LapNumber == lap.LapNumber
                    && sample.SessionElapsedSeconds >= lap.StartedAtSeconds - 0.01
                    && sample.SessionElapsedSeconds <= lap.EndedAtSeconds + 0.01)
                .OrderBy(sample => sample.SessionElapsedSeconds)
                .ToArray();
            var sectors = CalculateSectorOverview(lapSamples, trackLength);
            var lastSample = lapSamples.LastOrDefault();
            double? delta = bestTime is null || lap.LapTimeSeconds <= 0 ? null : lap.LapTimeSeconds - bestTime.Value;
            var validityText = !lap.IsComplete ? "— 片段" : lap.IsValid ? "✓ 有效" : "× 无效";
            var validityColor = !lap.IsComplete ? "#F5B942" : lap.IsValid ? "#20C76F" : "#F04444";
            rows.Add(new MultiLapRow(
                lap.LapNumber,
                lap.LapTimeSeconds,
                lap.LapTimeSeconds > 0 ? FormatTime(lap.LapTimeSeconds) : "--:--.---",
                delta is null ? "--" : Math.Abs(delta.Value) < 0.0005 ? "BEST" : $"{delta.Value:+0.000;-0.000;0.000}",
                delta is null || delta.Value <= 0.0005 ? "#20C76F" : "#F04444",
                SectorSpeedText(sectors[0].AverageSpeedKph),
                SectorSpeedText(sectors[1].AverageSpeedKph),
                SectorSpeedText(sectors[2].AverageSpeedKph),
                sectors[0].Seconds,
                sectors[1].Seconds,
                sectors[2].Seconds,
                lap.IsValid,
                lap.IsComplete,
                validityText,
                validityColor,
                ResolveTireText(lastSample),
                lastSample is null ? "--" : $"{lastSample.FuelLiters:F1} L",
                WeatherText(lastSample?.Environment),
                lastSample is null ? "--" : $"{lastSample.Environment.AmbientTemperatureCelsius:F1}°",
                lastSample is null ? "--" : $"{lastSample.Environment.TrackTemperatureCelsius:F1}°"));

            var validPositions = lapSamples
                .Where(sample => sample.Quality.IsWorldPositionValid && sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0)
                .ToArray();
            if (validPositions.Length > 1)
            {
                var stride = Math.Max(1, validPositions.Length / 800);
                var points = validPositions.Where((_, index) => index % stride == 0 || index == validPositions.Length - 1)
                    .Select(sample => new TrackPoint(sample.LapDistanceMeters, sample.WorldPosition.X, -sample.WorldPosition.Z, sample.WorldPosition.Y))
                    .ToArray();
                traces.Add(new LapTrace(lap.LapNumber, lap.IsValid, lap.IsComplete, lap.LapNumber == bestLap?.LapNumber, points));
            }
        }

        var selectedLapNumber = SelectedMultiLapRow?.LapNumber;
        _multiLapRows = rows;
        _multiLapTraces = traces;
        _lapHistogramBars = BuildLapHistogram(rows);
        SelectedMultiLapRow = rows.FirstOrDefault(row => row.LapNumber == selectedLapNumber)
            ?? rows.FirstOrDefault(row => row.LapNumber == bestLap?.LapNumber)
            ?? rows.LastOrDefault(row => row.IsComplete)
            ?? rows.LastOrDefault();
        OnPropertyChanged(nameof(MultiLapRows));
        OnPropertyChanged(nameof(MultiLapTraces));
        OnPropertyChanged(nameof(LapHistogramBars));
        OnPropertyChanged(nameof(CompleteLapCount));
        OnPropertyChanged(nameof(ValidCompleteLapCount));
        OnPropertyChanged(nameof(TotalOverviewLapCount));
        OnPropertyChanged(nameof(BestLapTimeText));
        OnPropertyChanged(nameof(LapSpreadText));
        OnPropertyChanged(nameof(AverageLapTimeText));
        OnPropertyChanged(nameof(TheoreticalBestLapText));
        OnPropertyChanged(nameof(ValidLapRatioText));
        OnPropertyChanged(nameof(AllLapCountText));
        OnPropertyChanged(nameof(ValidLapCountText));
        OnPropertyChanged(nameof(InvalidLapCountText));
    }

    private double ResolveTrackLength(IReadOnlyList<TelemetrySample> samples)
    {
        var sessionLength = CurrentSession?.Track.CenterLine.Count > 1
            ? CurrentSession.Track.CenterLine.Max(point => point.DistanceMeters)
            : 0;
        var liveLength = _liveContext?.TrackLengthMeters ?? 0;
        var sampledLength = samples.Count > 0 ? samples.Max(sample => sample.LapDistanceMeters) : 0;
        return Math.Max(1, Math.Max(Math.Max(sessionLength, liveLength), sampledLength));
    }

    private static SectorOverview[] CalculateSectorOverview(IReadOnlyList<TelemetrySample> samples, double trackLength)
    {
        var result = new SectorOverview[3];
        if (samples.Count == 0) return result;
        var hasOfficialSectors = samples.Any(sample => sample.CurrentSector is >= 0 and <= 2);
        int SectorOf(TelemetrySample sample) => hasOfficialSectors
            ? sample.CurrentSector
            : Math.Clamp((int)(Math.Max(0, sample.LapDistanceMeters) / trackLength * 3), 0, 2);
        for (var sectorIndex = 0; sectorIndex < 3; sectorIndex++)
        {
            var sectorSamples = samples.Where(sample => SectorOf(sample) == sectorIndex)
                .OrderBy(sample => sample.SessionElapsedSeconds)
                .ToArray();
            if (sectorSamples.Length == 0) continue;
            var seconds = 0d;
            for (var index = 1; index < samples.Count; index++)
            {
                if (SectorOf(samples[index - 1]) != sectorIndex || SectorOf(samples[index]) != sectorIndex) continue;
                var delta = samples[index].SessionElapsedSeconds - samples[index - 1].SessionElapsedSeconds;
                if (delta is > 0 and < 1) seconds += delta;
            }
            var speeds = sectorSamples.Select(sample => sample.SpeedMetersPerSecond * 3.6)
                .Where(speed => double.IsFinite(speed) && speed >= 0).ToArray();
            result[sectorIndex] = new SectorOverview(seconds, speeds.Length > 0 ? speeds.Average() : 0);
        }
        return result;
    }

    private IReadOnlyList<LapHistogramBar> BuildLapHistogram(IReadOnlyList<MultiLapRow> rows)
    {
        const int binCount = 14;
        var times = rows.Where(row => row.IsComplete && row.IsValid && row.LapTimeSeconds > 0)
            .Select(row => row.LapTimeSeconds).ToArray();
        if (times.Length == 0) return Enumerable.Range(0, binCount)
            .Select(_ => new LapHistogramBar(5, "#172638", "暂无完整有效圈")).ToArray();

        var min = times.Min();
        var max = times.Max();
        var spread = Math.Max(0.2, max - min);
        var padding = spread * 0.12;
        var lower = min - padding;
        var upper = max + padding;
        var width = Math.Max(0.001, (upper - lower) / binCount);
        var counts = new int[binCount];
        foreach (var time in times)
        {
            var index = Math.Clamp((int)((time - lower) / width), 0, binCount - 1);
            counts[index]++;
        }
        var peak = Math.Max(1, counts.Max());
        return counts.Select((count, index) => new LapHistogramBar(
            count == 0 ? 5 : 12 + 54d * count / peak,
            count == 0 ? "#172638" : "#2B8CFF",
            $"{FormatTime(lower + (index + 0.5) * width)} · {count} 圈")).ToArray();
    }

    private string ResolveTireText(TelemetrySample? sample)
    {
        var front = sample?.FrontTireCompound?.Trim() ?? string.Empty;
        var rear = sample?.RearTireCompound?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(front) || !string.IsNullOrWhiteSpace(rear))
        {
            if (string.Equals(front, rear, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rear))
                return NormalizeCompound(front);
            if (string.IsNullOrWhiteSpace(front)) return NormalizeCompound(rear);
            return $"前 {NormalizeCompound(front)} / 后 {NormalizeCompound(rear)}";
        }

        var setupCompound = SetupSnapshot.Values.FirstOrDefault(value => value.Available
            && value.Key.Contains("TIRE", StringComparison.OrdinalIgnoreCase)
            && value.Key.Contains("COMPOUND", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(value.DisplayValue));
        if (setupCompound is not null) return NormalizeCompound(setupCompound.DisplayValue);

        return "配方未知";
    }

    private static string NormalizeCompound(string value)
    {
        var compact = value.Trim();
        if (compact.Contains("soft", StringComparison.OrdinalIgnoreCase) || compact.Contains('软')) return "红胎 · 软胎";
        if (compact.Contains("medium", StringComparison.OrdinalIgnoreCase) || compact.Contains("中性", StringComparison.OrdinalIgnoreCase)) return "黄胎 · 中性胎";
        if (compact.Contains("hard", StringComparison.OrdinalIgnoreCase) || compact.Contains('硬')) return "白胎 · 硬胎";
        if (compact.Contains("wet", StringComparison.OrdinalIgnoreCase) || compact.Contains("rain", StringComparison.OrdinalIgnoreCase) || compact.Contains('雨')) return "蓝胎 · 雨胎";
        return string.IsNullOrWhiteSpace(compact) ? "配方未知" : compact;
    }

    private string ResolveTireAccent(TelemetrySample? sample)
    {
        var front = sample?.FrontTireCompound?.Trim() ?? string.Empty;
        var rear = sample?.RearTireCompound?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(front) && !string.IsNullOrWhiteSpace(rear)
            && !string.Equals(NormalizeCompound(front), NormalizeCompound(rear), StringComparison.OrdinalIgnoreCase)) return "#7B8794";
        var compound = !string.IsNullOrWhiteSpace(front) ? front : !string.IsNullOrWhiteSpace(rear) ? rear : ResolveTireText(sample);
        if (compound.Contains("soft", StringComparison.OrdinalIgnoreCase) || compound.Contains('软') || compound.Contains("红胎", StringComparison.Ordinal)) return "#F04444";
        if (compound.Contains("medium", StringComparison.OrdinalIgnoreCase) || compound.Contains("中性", StringComparison.OrdinalIgnoreCase) || compound.Contains("黄胎", StringComparison.Ordinal)) return "#F2C94C";
        if (compound.Contains("hard", StringComparison.OrdinalIgnoreCase) || compound.Contains('硬') || compound.Contains("白胎", StringComparison.Ordinal)) return "#E9EEF5";
        if (compound.Contains("wet", StringComparison.OrdinalIgnoreCase) || compound.Contains("rain", StringComparison.OrdinalIgnoreCase) || compound.Contains('雨')) return "#3296FF";
        return "#7B8794";
    }

    private static string WeatherText(EnvironmentSample? environment, string? metadataWeather = null)
    {
        if (environment is null) return "--";
        if (double.IsFinite(environment.RainFraction) && environment.RainFraction >= 0.67) return "大雨";
        if (double.IsFinite(environment.RainFraction) && environment.RainFraction >= 0.34) return "中雨";
        if (double.IsFinite(environment.RainFraction) && environment.RainFraction >= 0.05) return "小雨";
        if (!string.IsNullOrWhiteSpace(metadataWeather)
            && (metadataWeather.Contains('雨') || metadataWeather.Contains("rain", StringComparison.OrdinalIgnoreCase))) return "雨天";
        return (double.IsFinite(environment.CloudDarknessFraction) && environment.CloudDarknessFraction >= 0.35)
            || (!string.IsNullOrWhiteSpace(metadataWeather)
                && (metadataWeather.Contains('云') || metadataWeather.Contains('阴') || metadataWeather.Contains("cloud", StringComparison.OrdinalIgnoreCase)))
            ? "阴天" : "晴天";
    }

    private static string WeatherIcon(EnvironmentSample? environment, string? metadataWeather)
    {
        if (environment is null) return "--";
        if (double.IsFinite(environment.RainFraction) && environment.RainFraction >= 0.05)
        {
            var count = environment.RainFraction >= 0.67 ? 3 : environment.RainFraction >= 0.34 ? 2 : 1;
            return string.Concat(Enumerable.Repeat("💧", count));
        }
        if (!string.IsNullOrWhiteSpace(metadataWeather)
            && (metadataWeather.Contains('雨') || metadataWeather.Contains("rain", StringComparison.OrdinalIgnoreCase))) return "💧";
        if ((double.IsFinite(environment.CloudDarknessFraction) && environment.CloudDarknessFraction >= 0.35)
            || (!string.IsNullOrWhiteSpace(metadataWeather)
                && (metadataWeather.Contains('云') || metadataWeather.Contains('阴') || metadataWeather.Contains("cloud", StringComparison.OrdinalIgnoreCase)))) return "☁";
        return "☀";
    }

    private static string LocalizeSessionType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "--";
        var compact = value.Trim();
        if (compact.Contains("练习", StringComparison.Ordinal) || compact.Contains("排位", StringComparison.Ordinal)
            || compact.Contains("正赛", StringComparison.Ordinal) || compact.Contains("热身", StringComparison.Ordinal)
            || compact.Contains("测试", StringComparison.Ordinal)) return compact;
        var suffix = new string(compact.Where(char.IsDigit).ToArray());
        var numberedSuffix = string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" {suffix}";
        if (compact.Contains("practice", StringComparison.OrdinalIgnoreCase)) return $"练习赛{numberedSuffix}";
        if (compact.Contains("qual", StringComparison.OrdinalIgnoreCase)) return $"排位赛{numberedSuffix}";
        if (compact.Contains("race", StringComparison.OrdinalIgnoreCase)) return $"正赛{numberedSuffix}";
        if (compact.Contains("warm", StringComparison.OrdinalIgnoreCase)) return "热身赛";
        if (compact.Contains("test", StringComparison.OrdinalIgnoreCase)) return "测试日";
        return compact;
    }

    private static string SectorSpeedText(double speedKph) => speedKph > 0 ? $"{speedKph:F0} km/h" : "--";

    private IReadOnlyList<TelemetrySample> SamplesForComparisonLap(LapRecord? lap)
    {
        if (CurrentSession is null || lap is null) return [];
        return CurrentSession.Samples.Where(sample => sample.LapNumber == lap.LapNumber
            && sample.SessionElapsedSeconds >= lap.StartedAtSeconds - 0.01
            && sample.SessionElapsedSeconds <= lap.EndedAtSeconds + 0.01).ToArray();
    }

    private void ResetLibraryFilters()
    {
        LibrarySearchText = string.Empty;
        SelectedLibraryTrackFilter = "全部赛道";
        SelectedLibraryVehicleFilter = "全部车辆";
        SelectedLibraryTimeFilter = "全部时间";
        SelectedLibrarySessionFilter = "全部会话";
        SelectedLibraryValidityFilter = "全部";
        ApplyLibraryFilters();
    }

    private void ApplyLibraryFilters(bool preserveSelection = true)
    {
        var selectedPath = preserveSelection ? SelectedStoredSession?.Path : null;
        var query = StoredSessions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(LibrarySearchText))
        {
            var search = LibrarySearchText.Trim();
            query = query.Where(entry => entry.Metadata.TrackName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Metadata.VehicleName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Metadata.SessionType.Contains(search, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(entry.Path).Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedLibraryTrackFilter != "全部赛道") query = query.Where(entry => entry.Metadata.TrackName == SelectedLibraryTrackFilter);
        if (SelectedLibraryVehicleFilter != "全部车辆") query = query.Where(entry => entry.Metadata.VehicleName == SelectedLibraryVehicleFilter);
        if (SelectedLibrarySessionFilter != "全部会话") query = query.Where(entry => entry.Metadata.SessionType == SelectedLibrarySessionFilter);
        query = SelectedLibraryTimeFilter switch
        {
            "最近 7 天" => query.Where(entry => entry.Metadata.StartedAtUtc >= DateTimeOffset.Now.AddDays(-7)),
            "最近 30 天" => query.Where(entry => entry.Metadata.StartedAtUtc >= DateTimeOffset.Now.AddDays(-30)),
            _ => query
        };
        query = SelectedLibraryValidityFilter switch
        {
            "有有效圈" => query.Where(entry => entry.ValidLapCount > 0),
            "完整记录" => query.Where(entry => entry.Metadata.IsComplete),
            _ => query
        };

        FilteredStoredSessions = new ObservableCollection<StoredSessionInfo>(query);
        SelectedStoredSession = FilteredStoredSessions.FirstOrDefault(entry => string.Equals(entry.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? FilteredStoredSessions.FirstOrDefault();
    }

    private void OpenStorageFolder()
    {
        Directory.CreateDirectory(StoragePath);
        Process.Start(new ProcessStartInfo(StoragePath) { UseShellExecute = true });
    }

    private async Task RefreshLibraryAsync(string? preferredPath = null)
    {
        var entries = await _repository.ListEntriesAsync();
        StoredSessions = new ObservableCollection<StoredSessionInfo>(entries);
        LibraryTrackFilters = new ObservableCollection<string>(new[] { "全部赛道" }.Concat(entries.Select(entry => entry.Metadata.TrackName).Distinct().OrderBy(value => value)));
        LibraryVehicleFilters = new ObservableCollection<string>(new[] { "全部车辆" }.Concat(entries.Select(entry => entry.Metadata.VehicleName).Distinct().OrderBy(value => value)));
        LibrarySessionFilters = new ObservableCollection<string>(new[] { "全部会话" }.Concat(entries.Select(entry => entry.Metadata.SessionType).Distinct().OrderBy(value => value)));
        if (!LibraryTrackFilters.Contains(SelectedLibraryTrackFilter)) SelectedLibraryTrackFilter = "全部赛道";
        if (!LibraryVehicleFilters.Contains(SelectedLibraryVehicleFilter)) SelectedLibraryVehicleFilter = "全部车辆";
        if (!LibrarySessionFilters.Contains(SelectedLibrarySessionFilter)) SelectedLibrarySessionFilter = "全部会话";
        ApplyLibraryFilters(false);
        SessionLibrary = new ObservableCollection<SessionMetadata>(entries.Select(entry => entry.Metadata));
        SelectedStoredSession = FilteredStoredSessions.FirstOrDefault(entry => string.Equals(entry.Path, preferredPath, StringComparison.OrdinalIgnoreCase))
            ?? FilteredStoredSessions.FirstOrDefault();
        OnPropertyChanged(nameof(StoredSessionCount));
        OnPropertyChanged(nameof(StorageSizeText));
    }

    private async Task OpenSelectedSessionAsync()
    {
        if (SelectedStoredSession is null) return;
        IsBusy = true;
        try
        {
            var session = await _repository.OpenAsync(SelectedStoredSession.Path);
            LoadSession(session);
            CurrentPage = PageKind.MultiLap;
            StatusMessage = $"已载入记录：{session.Metadata.TrackName}，可查看多圈、单圈回放和双圈对比。";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Unable to open stored session {Path}", SelectedStoredSession.Path);
            StatusMessage = $"打开记录失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportSelectedSessionAsync()
    {
        if (SelectedStoredSession is null) return;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ApexTrace", "Exports");
        Directory.CreateDirectory(directory);
        var source = SelectedStoredSession.Path;
        var destination = Path.Combine(directory, Path.GetFileName(source));
        if (File.Exists(destination))
        {
            destination = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(source)}_{DateTime.Now:yyyyMMdd_HHmmss}.apextrace");
        }
        File.Copy(source, destination, false);
        StatusMessage = $"已导出记录：{destination}";
        await Task.CompletedTask;
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedStoredSession is null) return;
        var target = SelectedStoredSession;
        var answer = MessageBox.Show($"将这条记录移到回收站？\n\n{target.Metadata.TrackName}\n{target.Metadata.StartedAtUtc:yyyy-MM-dd HH:mm}",
            "删除记录", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        FileSystem.DeleteFile(target.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        await RefreshLibraryAsync();
        StatusMessage = "记录已移到回收站，可以从 Windows 回收站恢复。";
    }

    private async Task SaveSessionAsync()
    {
        if (CurrentSession is null) return;
        LastSavedPath = await _repository.SaveAsync(CurrentSession);
        StatusMessage = $"已保存到本地记录库：{LastSavedPath}";
        IsEndModalVisible = false;
        await RefreshLibraryAsync(LastSavedPath);
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
        RefreshRecoverableSessions();
        await Task.CompletedTask;
    }

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanEndRecording));
        OnPropertyChanged(nameof(TrackName));
        OnPropertyChanged(nameof(VehicleName));
        OnPropertyChanged(nameof(SessionType));
        OnPropertyChanged(nameof(DataSourceText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(HeaderHashShort));
        OnPropertyChanged(nameof(TrackPoints));
        OnPropertyChanged(nameof(RealtimeTrackPoints));
        OnPropertyChanged(nameof(RealtimeTrackProgress));
        OnPropertyChanged(nameof(PedalTraceSamples));
        OnPropertyChanged(nameof(Laps));
        OnPropertyChanged(nameof(MultiLapRows));
        OnPropertyChanged(nameof(MultiLapTraces));
        OnPropertyChanged(nameof(LapHistogramBars));
        OnPropertyChanged(nameof(SelectedMultiLapNumber));
        OnPropertyChanged(nameof(DashboardLaps));
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(Recommendations));
        OnPropertyChanged(nameof(PrimaryRecommendation));
        OnPropertyChanged(nameof(PrimarySetupRecommendation));
        OnPropertyChanged(nameof(RecommendationHeadline));
        OnPropertyChanged(nameof(RecommendationCountText));
        OnPropertyChanged(nameof(RecommendationImpactText));
        OnPropertyChanged(nameof(RecommendationScopeText));
        OnPropertyChanged(nameof(RecommendationEvidenceText));
        OnPropertyChanged(nameof(RecommendationValidationText));
        OnPropertyChanged(nameof(SetupRecommendationHeadline));
        OnPropertyChanged(nameof(SetupRecommendationEvidenceText));
        OnPropertyChanged(nameof(SetupRecommendationValidationText));
        OnPropertyChanged(nameof(ComparisonCurrentLabel));
        OnPropertyChanged(nameof(ComparisonReferenceLabel));
        OnPropertyChanged(nameof(LapDeltaText));
        OnPropertyChanged(nameof(Sector1DeltaText));
        OnPropertyChanged(nameof(Sector2DeltaText));
        OnPropertyChanged(nameof(Sector3DeltaText));
        OnPropertyChanged(nameof(ComparisonCurrentPoints));
        OnPropertyChanged(nameof(ComparisonReferencePoints));
        OnPropertyChanged(nameof(SampleCount));
        OnPropertyChanged(nameof(CompleteLapCount));
        OnPropertyChanged(nameof(ValidCompleteLapCount));
        OnPropertyChanged(nameof(TotalOverviewLapCount));
        OnPropertyChanged(nameof(BestLapTimeText));
        OnPropertyChanged(nameof(AverageLapTimeText));
        OnPropertyChanged(nameof(TheoreticalBestLapText));
        OnPropertyChanged(nameof(ValidLapRatioText));
        OnPropertyChanged(nameof(AllLapCountText));
        OnPropertyChanged(nameof(ValidLapCountText));
        OnPropertyChanged(nameof(InvalidLapCountText));
        OnPropertyChanged(nameof(LapSpreadText));
        OnPropertyChanged(nameof(TrackQualityText));
        OnPropertyChanged(nameof(ReplayAvailabilityText));
        OnPropertyChanged(nameof(ReplayableLaps));
        OnPropertyChanged(nameof(ReplayEventRows));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(CompletenessText));
        OnPropertyChanged(nameof(ImportSummary));
        OnPropertyChanged(nameof(SamplingRateText));
        RefreshTelemetry();
        RefreshCommands();
    }

    private void RefreshTelemetry()
    {
        foreach (var property in new[] { nameof(SpeedText), nameof(SpeedValue), nameof(GearText), nameof(RpmText), nameof(RpmValue),
                     nameof(ThrottleValue), nameof(BrakeValue), nameof(ThrottleText), nameof(BrakeText), nameof(SteeringText),
                     nameof(SteeringDegrees), nameof(LateralGValue), nameof(LongitudinalGValue), nameof(LateralGText),
                     nameof(LongitudinalGText), nameof(LapTimeText), nameof(BestDeltaText), nameof(AbsLevelText),
                     nameof(TcLevelText), nameof(AbsStatusText), nameof(TcStatusText), nameof(IsErsAvailable), nameof(ErsBatteryText),
                     nameof(ErsStateText), nameof(BrakeBiasText), nameof(BrakeBiasDetailText), nameof(FrontLeftTireTempText),
                     nameof(FrontRightTireTempText), nameof(RearLeftTireTempText), nameof(RearRightTireTempText),
                     nameof(FrontLeftBrakeTempText), nameof(FrontRightBrakeTempText), nameof(RearLeftBrakeTempText), nameof(RearRightBrakeTempText),
                     nameof(FrontLeftTireWearText), nameof(FrontRightTireWearText), nameof(RearLeftTireWearText), nameof(RearRightTireWearText),
                     nameof(FuelText), nameof(FuelRemainingLapsText), nameof(LapDistanceText), nameof(CurrentLapText),
                     nameof(CurrentTireText), nameof(CurrentTireAccent), nameof(CurrentWeatherText), nameof(WeatherIconText),
                     nameof(WeatherPrecipitationText), nameof(WeatherDetailText), nameof(AmbientText), nameof(TrackTemperatureText), nameof(ReplayLapText), nameof(ReplayElapsedText),
                     nameof(ReplayTotalText), nameof(RealtimeTrackProgress), nameof(DurationText), nameof(SessionType) })
        {
            OnPropertyChanged(property);
        }
    }

    private void RefreshCommands()
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        EndRecordingCommand.NotifyCanExecuteChanged();
        ToggleReplayCommand.NotifyCanExecuteChanged();
        StepReplayCommand.NotifyCanExecuteChanged();
        SeekReplayCommand.NotifyCanExecuteChanged();
        SetPlaybackRateCommand.NotifyCanExecuteChanged();
        JumpToEventCommand.NotifyCanExecuteChanged();
        SaveSessionCommand.NotifyCanExecuteChanged();
        ExportDefaultCommand.NotifyCanExecuteChanged();
        RecoverLatestCommand.NotifyCanExecuteChanged();
        OpenSelectedSessionCommand.NotifyCanExecuteChanged();
        ExportSelectedSessionCommand.NotifyCanExecuteChanged();
        DeleteSelectedSessionCommand.NotifyCanExecuteChanged();
    }

    private void RefreshRecoverableSessions()
    {
        RecoverableSessionCount = SessionRecorder.FindRecoverableSessions(TempRoot)
            .Count(path => !string.Equals(path, _recorder?.SessionDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private string TireTemperatureText(int wheelIndex)
    {
        if (CurrentSample?.Wheels is not { Length: > 0 } wheels || wheelIndex < 0 || wheelIndex >= wheels.Length) return "--";
        var wheel = wheels[wheelIndex];
        var surfaces = new[] { wheel.SurfaceLeftCelsius, wheel.SurfaceCenterCelsius, wheel.SurfaceRightCelsius }
            .Where(value => value > 0).ToArray();
        var temperature = surfaces.Length > 0 ? surfaces.Average() : wheel.CarcassTemperatureCelsius;
        return temperature > 0 ? $"{temperature:F0}" : "--";
    }

    private string BrakeTemperatureText(int wheelIndex)
    {
        if (CurrentSample?.Wheels is not { Length: > 0 } wheels || wheelIndex < 0 || wheelIndex >= wheels.Length) return "--";
        var temperature = wheels[wheelIndex].BrakeTemperatureCelsius;
        return double.IsFinite(temperature) && temperature > 0 ? $"{temperature:F0}" : "--";
    }

    private string TireWearText(int wheelIndex)
    {
        if (CurrentSample?.Wheels is not { Length: > 0 } wheels || wheelIndex < 0 || wheelIndex >= wheels.Length) return "--";
        var integrity = wheels[wheelIndex].WearFraction;
        return double.IsFinite(integrity) ? $"{Math.Clamp(integrity, 0, 1):P0}" : "--";
    }

    private string SectorTimeText(int index)
    {
        double? value;
        if (IsRecording)
        {
            value = index >= 0 && index < _liveSectorTimes.Length ? _liveSectorTimes[index] : null;
        }
        else
        {
            var sectors = CalculateSectorTimes(ReplaySamples);
            value = index >= 0 && index < sectors.Length ? sectors[index] : null;
        }
        return value is > 0 ? FormatTime(value.Value) : "--:--.---";
    }

    private static double?[] CalculateSectorTimes(IReadOnlyList<TelemetrySample> samples)
    {
        var ordered = samples.Where(sample => sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0)
            .OrderBy(sample => sample.SessionElapsedSeconds).ToArray();
        if (ordered.Length < 3) return [null, null, null];
        var length = ordered.Max(sample => sample.LapDistanceMeters);
        if (length <= 0) return [null, null, null];
        var startDistance = ordered[0].LapDistanceMeters;
        var normalized = ordered.Select(sample =>
        {
            var distance = sample.LapDistanceMeters - startDistance;
            if (distance < 0) distance += length;
            return (Sample: sample, Distance: distance);
        }).ToArray();
        var sector1End = normalized.FirstOrDefault(item => item.Distance >= length / 3d).Sample;
        var sector2End = normalized.FirstOrDefault(item => item.Distance >= length * 2d / 3d).Sample;
        if (sector1End is null || sector2End is null) return [null, null, null];
        var start = ordered[0].SessionElapsedSeconds;
        var end = ordered[^1].SessionElapsedSeconds;
        return
        [
            Math.Max(0, sector1End.SessionElapsedSeconds - start),
            Math.Max(0, sector2End.SessionElapsedSeconds - sector1End.SessionElapsedSeconds),
            Math.Max(0, end - sector2End.SessionElapsedSeconds)
        ];
    }

    private static double CalculateFuelPerLap(IReadOnlyList<TelemetrySample> samples, IReadOnlyList<LapRecord> laps)
    {
        var consumption = new List<double>();
        foreach (var lap in laps.Where(item => item.IsComplete))
        {
            var lapSamples = samples.Where(sample => sample.LapNumber == lap.LapNumber
                && sample.SessionElapsedSeconds >= lap.StartedAtSeconds - 0.01
                && sample.SessionElapsedSeconds <= lap.EndedAtSeconds + 0.01
                && sample.FuelLiters > 0).OrderBy(sample => sample.SessionElapsedSeconds).ToArray();
            if (lapSamples.Length < 2) continue;
            var used = lapSamples[0].FuelLiters - lapSamples[^1].FuelLiters;
            if (used > 0.01) consumption.Add(used);
        }
        return consumption.Count > 0 ? consumption.Average() : 0;
    }

    private static string EventDisplayName(DrivingEventType type) => type switch
    {
        DrivingEventType.LapStarted => "开始圈",
        DrivingEventType.LapCompleted => "完成圈",
        DrivingEventType.BrakeStarted or DrivingEventType.PeakBrake => "刹车点",
        DrivingEventType.BrakeReleased => "松刹车",
        DrivingEventType.TurnIn => "入弯",
        DrivingEventType.Apex => "弯心",
        DrivingEventType.ThrottleStarted => "补油点",
        DrivingEventType.FullThrottle => "全油门",
        DrivingEventType.GearShift => "换挡",
        DrivingEventType.AbsActivation => "ABS 介入",
        DrivingEventType.TcActivation => "TC 介入",
        DrivingEventType.OffTrack => "出界",
        DrivingEventType.LapInvalidated => "圈无效",
        DrivingEventType.PitEntry => "进站",
        DrivingEventType.PitExit => "出站",
        DrivingEventType.Impact => "碰撞",
        _ => type.ToString()
    };

    private static string EventAccent(DrivingEventType type) => type switch
    {
        DrivingEventType.BrakeStarted or DrivingEventType.PeakBrake or DrivingEventType.OffTrack or DrivingEventType.Impact => "#F04444",
        DrivingEventType.Apex or DrivingEventType.TurnIn => "#F5B942",
        DrivingEventType.ThrottleStarted or DrivingEventType.FullThrottle => "#20C76F",
        _ => "#2B8CFF"
    };

    private static string EventValueText(DrivingEvent item) => item.Type switch
    {
        DrivingEventType.BrakeStarted or DrivingEventType.BrakeReleased or DrivingEventType.ThrottleStarted or DrivingEventType.FullThrottle => $"{item.Value:P0}",
        DrivingEventType.GearShift => $"{item.Value:F0} 挡",
        DrivingEventType.AbsActivation or DrivingEventType.TcActivation => "ACTIVE",
        _ => "—"
    };

    private static string TempRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexTrace", "Temp");

    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff");

    private static string FormatSeconds(double? seconds) => seconds is { } value ? FormatTime(value) : "--:--.---";

    private static double FirstBrakeDistance(IReadOnlyList<TelemetrySample> samples) => samples
        .Where(sample => sample.Quality.IsLapDistanceValid && sample.LapDistanceMeters >= 0 && sample.Brake >= 0.05)
        .Select(sample => sample.LapDistanceMeters).DefaultIfEmpty(0).First();

    private static double FullThrottleRatio(IReadOnlyList<TelemetrySample> samples) => samples.Count == 0
        ? 0
        : samples.Count(sample => sample.Throttle >= 0.95) / (double)samples.Count;

    private static double MinimumSpeed(IReadOnlyList<TelemetrySample> samples) => samples
        .Select(sample => sample.SpeedMetersPerSecond * 3.6).DefaultIfEmpty(0).Min();

    private static double ExitSpeed(IReadOnlyList<TelemetrySample> samples) => samples.LastOrDefault()?.SpeedMetersPerSecond * 3.6 ?? 0;

    private static string FormatDistanceDelta(double value) => $"{value:+0.0;-0.0;0.0} m";
    private static string FormatSpeedDelta(double value) => $"{value:+0.0;-0.0;0.0} km/h";
    private static string DirectionHint(double value, string positive, string negative) => Math.Abs(value) < 0.001 ? "相同" : value > 0 ? positive : negative;

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

    private static double CalculateObservedRate(IReadOnlyList<TelemetrySample> samples)
    {
        var elapsed = 0.0;
        var intervals = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            var delta = samples[index].SessionElapsedSeconds - samples[index - 1].SessionElapsedSeconds;
            if (delta <= 0 || delta > 1) continue; // Ignore pause/load resets and discontinuities.
            elapsed += delta;
            intervals++;
        }
        return elapsed > 0 ? intervals / elapsed : 0;
    }

    private static double CalculateMissingRate(IReadOnlyList<TelemetrySample> samples, double expectedHz)
    {
        var expectedIntervals = 0L;
        var missingIntervals = 0L;
        var expectedDelta = 1 / expectedHz;
        for (var index = 1; index < samples.Count; index++)
        {
            var delta = samples[index].SessionElapsedSeconds - samples[index - 1].SessionElapsedSeconds;
            if (delta <= 0 || delta > 1) continue;
            var steps = Math.Max(1L, (long)Math.Round(delta / expectedDelta));
            expectedIntervals += steps;
            missingIntervals += steps - 1;
        }
        return expectedIntervals > 0 ? missingIntervals / (double)expectedIntervals : 0;
    }
}

public sealed record ReplayEventRow(string TimeText, string EventText, string PositionText, string ValueText, string Accent);

public sealed record MultiLapRow(
    int LapNumber,
    double LapTimeSeconds,
    string LapTimeText,
    string DeltaText,
    string DeltaColor,
    string Sector1SpeedText,
    string Sector2SpeedText,
    string Sector3SpeedText,
    double Sector1Seconds,
    double Sector2Seconds,
    double Sector3Seconds,
    bool IsValid,
    bool IsComplete,
    string ValidityText,
    string ValidityColor,
    string TireText,
    string FuelText,
    string WeatherText,
    string AirTemperatureText,
    string TrackTemperatureText);

public sealed record LapHistogramBar(double Height, string Fill, string ToolTip);

internal readonly record struct SectorOverview(double Seconds, double AverageSpeedKph);
