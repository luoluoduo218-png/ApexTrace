using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApexTrace.App;

public static partial class LocalizationManager
{
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";
    public const string GermanLanguage = "de-DE";

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["首页"] = "Home", ["实时采集"] = "Live Capture", ["多圈总览"] = "Multi-Lap Overview",
        ["单圈回放"] = "Lap Replay", ["双圈对比"] = "Lap Comparison", ["调教建议"] = "Setup Advice",
        ["记录库"] = "Session Library", ["设置"] = "Settings", ["游戏连接"] = "Game Connection",
        ["界面显示"] = "Display", ["数据与存储"] = "Data & Storage", ["关于 ApexTrace"] = "About ApexTrace",
        ["语言"] = "Language", ["中文"] = "Chinese", ["英文"] = "English", ["英语"] = "English", ["德语"] = "German", ["Deutsch"] = "Deutsch",
        ["选择应用界面语言。设置会自动保存，并立即生效。"] = "Choose the application language. The setting is saved automatically and applied immediately.",
        ["只读使用 Le Mans Ultimate 官方 LMU_Data 共享内存与可访问遥测文件。"] = "Uses the official Le Mans Ultimate LMU_Data shared memory and accessible telemetry files in read-only mode.",
        ["连接状态"] = "Connection Status", ["安装目录"] = "Installation Folder", ["选择目录…"] = "Choose Folder…",
        ["官方遥测文件"] = "Official Telemetry Files", ["游戏未运行时，可导入 LMU 官方写出的 DuckDB 文件用于回放和分析。"] = "When the game is not running, import official LMU DuckDB files for replay and analysis.",
        ["重新扫描并导入"] = "Rescan and Import", ["安全边界"] = "Safety Boundary",
        ["ApexTrace 不注入游戏进程、不写入共享内存，也不修改游戏调教。赛道形状缺失时仅由合法遥测轨迹重建。"] = "ApexTrace never injects into the game process, writes to shared memory, or changes the vehicle setup. Missing track geometry is reconstructed only from permitted telemetry traces.",
        ["显示偏好会自动保存，并应用到实时采集和单圈回放；双圈对比固定使用经典对比样式。"] = "Display preferences are saved automatically and applied to live capture and lap replay. Lap comparison always uses the classic comparison style.",
        ["油门 / 刹车显示"] = "Throttle / Brake Display", ["经典开度条"] = "Classic Bars", ["坐标曲线"] = "Scrolling Graph",
        ["选择独立开度条，或使用随播放时间滚动的油门与刹车开合度曲线。双圈对比始终保留原有显示。"] = "Choose separate input bars or a throttle and brake graph that scrolls with playback. Lap comparison keeps its original display.",
        ["延续当前界面：油门、刹车各自显示即时百分比与开度条。"] = "Shows the current throttle and brake percentages in separate input bars.",
        ["绿色油门与红色刹车共享 0–100% 纵轴；当前点固定在横轴最右端，已播放曲线随时间向左滚动。"] = "Green throttle and red brake share a 0–100% vertical axis. The current point stays at the right edge while history scrolls left.",
        ["本地记录以可校验的 .apextrace 包保存；删除操作会先确认并移到 Windows 回收站。"] = "Local recordings are stored as verifiable .apextrace packages. Deletion requires confirmation and moves files to the Windows Recycle Bin.",
        ["已保存记录"] = "Saved Sessions", ["占用空间"] = "Storage Used", ["记录库位置"] = "Library Location",
        ["包含会话、分圈、原始遥测、分析结果和校验和。"] = "Contains sessions, laps, raw telemetry, analysis results, and checksums.",
        ["在资源管理器中打开"] = "Open in File Explorer", ["管理本地记录"] = "Manage Local Sessions", ["刷新"] = "Refresh",
        ["日期"] = "Date", ["赛道"] = "Track", ["完整圈"] = "Complete Laps", ["样本"] = "Samples",
        ["打开分析"] = "Open Analysis", ["导出副本"] = "Export Copy", ["移到回收站"] = "Move to Recycle Bin",
        ["Le Mans Ultimate 只读遥测记录、回放与驾驶分析工具"] = "Read-only telemetry recording, replay, and driving analysis for Le Mans Ultimate",
        ["数据原则"] = "Data Principles", ["所有分析都来自用户主动记录或导入的 LMU 数据。完整圈不足时会明确降级；调教建议仅提供可验证的单变量实验，不会自动修改游戏。"] = "All analysis comes from LMU data explicitly recorded or imported by the user. The app clearly degrades when complete laps are insufficient; setup advice only proposes verifiable single-variable experiments and never changes the game automatically.",
        ["运行平台：Windows · .NET 10 · WPF"] = "Platform: Windows · .NET 10 · WPF", ["许可证与第三方声明：应用目录 THIRD_PARTY_NOTICES.md"] = "Licenses and third-party notices: THIRD_PARTY_NOTICES.md in the application folder",
        ["连接与记录就绪"] = "Connection & Recording", ["游戏状态"] = "Game Status", ["当前赛道"] = "Current Track",
        ["当前赛车"] = "Current Vehicle", ["数据源"] = "Data Source", ["最近导入"] = "Latest Import",
        ["开始记录指南"] = "Start Recording Guide", ["1   启动 LMU"] = "1   Launch LMU", ["2   进入赛道并拥有车辆"] = "2   Enter a track and take control of a vehicle",
        ["3   Enable Plugins 并等待 LMU_Data"] = "3   Enable Plugins and wait for LMU_Data", ["4   点击开始记录"] = "4   Click Start Recording",
        ["▷  开始记录"] = "▷  Start Recording", ["▶  开始记录"] = "▶  Start Recording", ["■  结束记录"] = "■  End Recording",
        ["▣  导入最新 LMU 遥测"] = "▣  Import Latest LMU Telemetry", ["↻  重置"] = "↻  Reset", ["⇧  导出"] = "⇧  Export",
        ["车辆与会话"] = "Vehicle & Session", ["当前轮胎"] = "Current Tires", ["气温"] = "Air Temperature", ["赛道温度"] = "Track Temperature",
        ["采集服务"] = "Capture Service", ["采样基准"] = "Sampling Target", ["记录样本"] = "Recorded Samples",
        ["赛道地图  |  "] = "Track Map  |  ", ["当前位置"] = "Current Position", ["当前圈轨迹"] = "Current Lap Trace",
        ["显示赛道图例"] = "Show Track Legend", ["收起赛道图例"] = "Hide Track Legend", ["点击收起或显示赛道图例"] = "Click to hide or show the track legend",
        ["左右拖动，调整赛道图与遥测数据比例"] = "Drag left or right to resize the track map and telemetry panels",
        ["圈速、有效性与车辆工况总览"] = "Overview of lap times, validity, and vehicle conditions", ["最佳单圈"] = "Best Lap", ["平均圈速"] = "Average Lap",
        ["理论最佳"] = "Theoretical Best", ["有效 / 总圈"] = "Valid / Total Laps", ["圈速分布"] = "Lap Time Distribution",
        ["全部"] = "All", ["有效圈"] = "Valid Laps", ["无效圈"] = "Invalid Laps", ["多圈轨迹叠加"] = "Multi-Lap Trace Overlay",
        ["有效圈实线叠加 · 无效圈灰色虚线 · 双击复位视图"] = "Valid laps are solid; invalid laps are dashed gray. Double-click to reset the view.",
        ["点击右侧圈号可高亮对应走线"] = "Click a lap number on the right to highlight its trace", ["圈"] = "Lap", ["圈速"] = "Lap Time",
        ["与最佳圈差"] = "Gap to Best", ["S1 均速"] = "S1 Avg Speed", ["S2 均速"] = "S2 Avg Speed", ["S3 均速"] = "S3 Avg Speed",
        ["轮胎"] = "Tires", ["燃油"] = "Fuel", ["天气"] = "Weather", ["气/轨温"] = "Air / Track Temp", ["有效性"] = "Validity",
        ["单圈时间"] = "Lap Time", ["回放控制"] = "Replay Controls", ["播放 / 暂停（空格）"] = "Play / Pause (Space)",
        ["◀ 事件"] = "◀ Event", ["事件 ▶"] = "Event ▶", ["地图与时间轴双向同步"] = "Map and timeline stay synchronized",
        ["完整单圈事件 · 拖动可定位地图、仪表和事件日志"] = "Complete-lap events · Drag to position the map, instruments, and event log",
        ["事件时间轴"] = "Event Timeline", ["事件日志"] = "Event Log", ["时间"] = "Time", ["事件"] = "Event", ["位置"] = "Position", ["数值"] = "Value",
        ["参考圈（橙色）"] = "Reference Lap (Orange)", ["当前圈（蓝色）"] = "Current Lap (Blue)", ["交换当前圈和参考圈"] = "Swap current and reference laps",
        ["总圈速差"] = "Total Lap Delta", ["正值表示当前圈慢于参考圈，负值表示当前圈更快"] = "Positive means the current lap is slower than the reference; negative means it is faster.",
        ["速度对比"] = "Speed Comparison", ["最大速度"] = "Maximum Speed", ["踏板输入对比"] = "Pedal Input Comparison",
        ["刹车点差异"] = "Braking Point Difference", ["全油门占比差异"] = "Full-Throttle Ratio Difference", ["最低速度差异"] = "Minimum Speed Difference", ["出圈速度差异"] = "Exit Speed Difference",
        ["弯道逐段对比"] = "Corner-by-Corner Comparison", ["正值表示当前圈在这个距离区间更慢。弯道边界由方向信号自动推断，可用于定位，不等同官方弯号。"] = "Positive means the current lap is slower in this distance segment. Corner boundaries are inferred from steering signals for navigation and do not correspond to official corner numbers.",
        ["赛段"] = "Segment", ["类型/距离"] = "Type / Distance", ["时间差"] = "Time Delta", ["当前"] = "Current", ["参考"] = "Reference", ["差值"] = "Delta",
        ["主要调教结论"] = "Primary Setup Finding", ["调教实验依据"] = "Setup Experiment Evidence", ["重复现象"] = "Recurring Pattern",
        ["LMU 可确认的当前控制值（只读）"] = "Current LMU-confirmed control values (read-only)", ["只读分析，不写回设置"] = "Read-only analysis; no setup changes are written",
        ["不足时不生成建议"] = "No advice when evidence is insufficient", ["至少 3 圈生成调教建议"] = "At least 3 laps required for setup advice",
        ["⇩ 导出调教建议"] = "⇩ Export Setup Advice", ["✎ 应用到方案"] = "✎ Add to Plan", ["✓ 预测与建议"] = "✓ Predictions & Advice",
        ["本地已保存会话"] = "Locally Saved Sessions", ["⌕  搜索会话、赛道或车辆…"] = "⌕  Search sessions, tracks, or vehicles…",
        ["全部赛道"] = "All Tracks", ["全部车辆"] = "All Vehicles", ["全部会话"] = "All Sessions", ["全部时间"] = "All Time",
        ["最近 7 天"] = "Last 7 Days", ["最近 30 天"] = "Last 30 Days", ["打开记录目录"] = "Open Session Folder",
        ["删除记录"] = "Delete Session", ["车辆"] = "Vehicle", ["会话"] = "Session", ["时长"] = "Duration", ["完整 / 有效圈"] = "Complete / Valid Laps",
        ["结束本次记录？"] = "End this recording?", ["保存到记录库、导出可携带的 .apextrace 文件，或放弃本次临时记录。"] = "Save to the session library, export a portable .apextrace file, or discard this temporary recording.",
        ["保存到记录库"] = "Save to Library", ["导出文件"] = "Export File", ["放弃记录"] = "Discard Recording", ["×  关闭"] = "×  Close",
        ["油门"] = "Throttle", ["刹车"] = "Brake", ["挡位"] = "Gear", ["车速"] = "Speed", ["发动机转速"] = "Engine RPM",
        ["方向盘"] = "Steering", ["横向 G"] = "Lateral G", ["纵向 G"] = "Longitudinal G", ["圈数进度"] = "Lap Progress",
        ["轮胎 | 刹车盘"] = "Tires | Brake Discs", ["外侧圆环：轮胎完整度与配方；中间自上而下：胎压、胎温、刹车盘温度"] = "Outer ring: tire condition and compound. Center values: pressure, tire temperature, and brake-disc temperature.",
        ["实线油门 / 虚线刹车"] = "Solid throttle / dashed brake", ["前 / 后"] = "Front / Rear", ["预计剩余"] = "Estimated Remaining",
        ["可用"] = "Available", ["不可用"] = "Unavailable", ["等待数据"] = "Waiting for data", ["等待遥测"] = "Waiting for telemetry",
        ["等待完整圈"] = "Waiting for a complete lap", ["等待选择"] = "Waiting for selection", ["等待 LMU"] = "Waiting for LMU",
        ["未识别车辆"] = "Unknown vehicle", ["数据不足"] = "Insufficient data", ["片段"] = "Fragment", ["完整记录"] = "Complete recording",
        ["✓ 有效"] = "✓ Valid", ["× 无效"] = "× Invalid", ["— 片段"] = "— Fragment", ["更晚"] = "Later", ["更早"] = "Earlier", ["更多"] = "More", ["更少"] = "Less",
        ["更快"] = "Faster", ["更慢"] = "Slower", ["相同"] = "Same", ["晴天"] = "Clear", ["阴天"] = "Cloudy", ["雨天"] = "Rain",
        ["小雨"] = "Light Rain", ["中雨"] = "Moderate Rain", ["大雨"] = "Heavy Rain", ["配方未知"] = "Unknown Compound",
        ["正赛"] = "Race", ["练习"] = "Practice", ["练习赛"] = "Practice", ["排位"] = "Qualifying", ["排位赛"] = "Qualifying", ["热身"] = "Warmup", ["热身赛"] = "Warmup", ["测试"] = "Test", ["测试日"] = "Test Day",
        ["开始圈"] = "Lap Started", ["完成圈"] = "Lap Completed", ["刹车点"] = "Braking Point", ["松刹车"] = "Brake Released",
        ["入弯"] = "Turn-in", ["弯心"] = "Apex", ["补油点"] = "Throttle Pick-up", ["全油门"] = "Full Throttle", ["换挡"] = "Gear Shift",
        ["出界"] = "Off Track", ["圈无效"] = "Lap Invalidated", ["进站"] = "Pit Entry", ["出站"] = "Pit Exit", ["碰撞"] = "Impact",
        ["ABS 介入"] = "ABS Active", ["TC 介入"] = "TC Active", ["LMU 已连接"] = "LMU Connected", ["LMU 未运行"] = "LMU Not Running",
        ["LMU 已检测 / 等待接口"] = "LMU Detected / Waiting for Interface", ["LMU 官方 DuckDB（只读）"] = "Official LMU DuckDB (read-only)",
        ["LMU_Data（只读）"] = "LMU_Data (read-only)", [".apextrace 包"] = ".apextrace package", ["正在初始化…"] = "Initializing…", ["正在检查 LMU…"] = "Checking LMU…",
        ["选择 Le Mans Ultimate 安装目录"] = "Choose the Le Mans Ultimate Installation Folder",
        ["⌁  打开分析"] = "⌁  Open Analysis", ["⌗ 适应地图"] = "⌗ Fit Map", ["▣  打开记录目录"] = "▣  Open Session Folder", ["▣  删除"] = "▣  Delete", ["⛶  放大"] = "⛶  Zoom In",
        ["本弯相对最佳圈"] = "This Corner vs. Best Lap", ["播放或暂停已载入记录"] = "Play or pause the loaded session", ["参考  "] = "Reference  ", ["参考轨迹（最佳圈）"] = "Reference Trace (Best Lap)", ["参考圈"] = "Reference Lap",
        ["当前  "] = "Current  ", ["当前圈"] = "Current Lap", ["当前诊断"] = "Current Diagnostic", ["当前值"] = "Current Value", ["对最佳圈"] = "vs. Best Lap",
        ["放大；也可使用鼠标滚轮"] = "Zoom in; you can also use the mouse wheel", ["官方接口"] = "Official Interface", ["轨迹来源"] = "Trace Source", ["滚轮缩放 · 拖动平移 · 双击复位"] = "Wheel to zoom · Drag to pan · Double-click to reset",
        ["含圈速基准与调教实验"] = "Includes lap-time baselines and setup experiments", ["回放刷新"] = "Replay Refresh", ["会话名称将使用赛道与记录时间自动生成。"] = "The session name is generated automatically from the track and recording time.",
        ["基于当前数据和赛道特性"] = "Based on current data and track characteristics", ["基于重复介入信号，不自动写回"] = "Based on recurring intervention signals; never written back automatically",
        ["进/弯心/出弯"] = "Entry / Apex / Exit", ["进弯 → 弯心最低 → 出弯"] = "Entry → Apex Minimum → Exit", ["禁止写回游戏"] = "No writes to the game", ["距离"] = "Distance",
        ["来源"] = "Source", ["来源  "] = "Source  ", ["连接 LMU 并进入赛道后开始只读采集"] = "Connect LMU and enter a track to begin read-only capture", ["圈数"] = "Laps",
        ["入弯→弯心→出弯"] = "Entry → Apex → Exit", ["赛道 / 长度"] = "Track / Length", ["赛道预览 · 实际 LMU GPS 轨迹"] = "Track Preview · Actual LMU GPS Trace",
        ["刹车 · 油门 · 换挡 · 车辆电子 · 圈状态"] = "Brake · Throttle · Shifts · Driver Aids · Lap Status", ["时间范围"] = "Time Range", ["实际文件"] = "Actual File",
        ["适应窗口；也可双击地图"] = "Fit to window; you can also double-click the map", ["缩小"] = "Zoom Out", ["胎压 · 胎温 · 盘温"] = "Tire Pressure · Tire Temp · Disc Temp",
        ["提示：若有效圈不足两个，仍可查看单圈弯道数据，但不会显示对比时间。"] = "Tip: With fewer than two valid laps, single-lap corner data remains available but comparison times are hidden.",
        ["弯道"] = "Corner", ["弯道分析"] = "Corner Analysis", ["弯心 / 入弯"] = "Apex / Entry", ["无注入 / 无 DLL"] = "No Injection / No DLL", ["下一圈基准"] = "Next-Lap Baseline",
        ["项目"] = "Item", ["选中圈"] = "Selected Lap", ["已识别弯道 / 转向区间"] = "Detected Corners / Steering Segments",
        ["由完整圈的方向、速度和制动信号自动识别；选择一行查看实际局部走线"] = "Detected automatically from steering, speed, and braking signals on complete laps. Select a row to view the actual local racing line.",
        ["游戏遥测与完整圈"] = "Game Telemetry & Complete Laps", ["运行时重建"] = "Runtime Reconstruction", ["证据"] = "Evidence", ["只读检测"] = "Read-only Detection",
        ["总 / 有效"] = "Total / Valid", ["总时长 / 文件"] = "Total Duration / File", ["最近完整有效圈中位数"] = "Median of Recent Complete Valid Laps", ["最快圈"] = "Fastest Lap",
        ["S1 / S2 / S3 显示区段平均速度"] = "S1 / S2 / S3 show average segment speed",
        ["等待导入或实时采集"] = "Waiting for an import or live capture", ["等待至少 3 个完整有效圈"] = "Waiting for at least 3 complete valid laps",
        ["需要至少 2 个有效圈"] = "At least 2 valid laps are required", ["需要至少 3 个相近条件下的完整有效圈"] = "At least 3 complete valid laps in similar conditions are required",
        ["数据不足时不会生成预测或调教建议。"] = "Predictions and setup advice are not generated when data is insufficient.", ["不会在证据不足时猜测调教参数。"] = "Setup parameters are never guessed without sufficient evidence.",
        ["完成稳定的有效圈后，将根据重复出现的 ABS、TC 和车辆控制信号给出单变量验证建议。"] = "After consistent valid laps, recurring ABS, TC, and vehicle-control signals are used to propose single-variable validation tests.",
        ["尚无遥测"] = "No telemetry yet", ["尚无可分析弯道"] = "No corners available for analysis", ["暂无完整有效圈"] = "No complete valid laps yet",
        ["仅可回放采集片段"] = "Only the captured fragment can be replayed", ["当前：不可用"] = "Current: unavailable", ["参考：不可用"] = "Reference: unavailable",
        ["实时记录尚未结束"] = "The live recording has not ended", ["没有可恢复的临时记录。"] = "No recoverable temporary recordings were found.",
        ["本次临时记录已丢弃。"] = "The temporary recording was discarded.", ["记录结束，但未收到新的 LMU 遥测样本。"] = "Recording ended, but no new LMU telemetry samples were received.",
        ["记录已移到回收站，可以从 Windows 回收站恢复。"] = "The session was moved to the Recycle Bin and can be restored from there.",
        ["正在从 LMU_Data 重建赛道轨迹。"] = "Reconstructing the track trace from LMU_Data.", ["正在恢复中断的临时记录…"] = "Recovering an interrupted temporary recording…",
        ["正在只读导入 LMU 原生 DuckDB…"] = "Importing the native LMU DuckDB in read-only mode…", ["LMU_Data 已连接"] = "LMU_Data Connected",
        ["LMU_Data 已连接，但遥测暂未推进；请确认游戏未暂停且车辆已进入可驾驶状态。"] = "LMU_Data is connected, but telemetry is not advancing. Make sure the game is not paused and the vehicle is drivable.",
        ["LMU_Data 已打开，但尚未解析到玩家车辆。请进入赛道并取得车辆控制后重试。"] = "LMU_Data is open, but the player vehicle has not been detected. Enter a track, take control of a vehicle, and try again.",
        ["降水概率 --"] = "Precipitation --", ["50 Hz 实时"] = "50 Hz live", ["100 Hz 文件"] = "100 Hz file", ["单圈实测"] = "Measured lap",
        ["中性"] = "Medium", ["黄胎"] = "Medium tire", ["黄胎 · 中性胎"] = "Medium tire · Medium compound"
    };

    public static string CurrentLanguage { get; private set; } = ChineseLanguage;
    public static bool IsEnglish => CurrentLanguage == EnglishLanguage;
    public static bool IsGerman => CurrentLanguage == GermanLanguage;
    public static event EventHandler? LanguageChanged;
    public static IReadOnlyList<ILanguagePack> AvailableLanguagePacks => LanguagePackRegistry.Packs;

    public static void SetLanguage(string? language, bool notify = true)
    {
        var normalized = LanguagePackRegistry.Resolve(language).LanguageCode;
        if (CurrentLanguage == normalized) return;
        CurrentLanguage = normalized;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized); }
        catch (CultureNotFoundException) { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ChineseLanguage); }
        if (notify) LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Translate(string? text) => LanguagePackRegistry.Resolve(CurrentLanguage).Translate(text ?? string.Empty);

    internal static string TranslateBuiltIn(string? text)
    {
        if (CurrentLanguage == ChineseLanguage || string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (English.TryGetValue(text, out var translated)) return Output(translated);
        if (text.Contains('\n')) return string.Join("\n", text.Split('\n').Select(Translate));
        if (text.Contains('；')) return string.Join("; ", text.Split('；').Select(Translate));

        foreach (var (prefix, englishPrefix) in Prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return Output(englishPrefix) + TranslateRemainder(text[prefix.Length..]);
        }

        var match = LapPattern().Match(text);
        if (match.Success) return Output($"Lap {match.Groups[1].Value}");
        match = VersionPattern().Match(text);
        if (match.Success) return Output($"Version {match.Groups[1].Value}");
        match = SessionCountPattern().Match(text);
        if (match.Success) return Output($"{match.Groups[1].Value} sessions");
        match = SampleCountPattern().Match(text);
        if (match.Success) return Output($"{match.Groups[1].Value} samples");
        match = CompleteLapReplayPattern().Match(text);
        if (match.Success) return Output($"{match.Groups[1].Value} complete laps available for replay");
        match = CurrentLapPattern().Match(text);
        if (match.Success) return Output($"Current: Lap {match.Groups[1].Value} · {match.Groups[2].Value}s");
        match = ReferenceLapPattern().Match(text);
        if (match.Success) return Output($"Reference: Lap {match.Groups[1].Value} · {match.Groups[2].Value}s");
        match = ProbabilityPattern().Match(text);
        if (match.Success) return Output($"Precipitation {match.Groups[1].Value}");
        match = SessionTypePattern().Match(text);
        if (match.Success)
        {
            var session = English.TryGetValue(match.Groups[1].Value, out var sessionName) ? sessionName : match.Groups[1].Value;
            return Output(session + match.Groups[2].Value);
        }
        match = AllLapsPattern().Match(text);
        if (match.Success) return Output($"All Laps  {match.Groups[1].Value}");
        match = ValidLapsPattern().Match(text);
        if (match.Success) return Output($"Valid Laps  {match.Groups[1].Value}");
        match = InvalidLapsPattern().Match(text);
        if (match.Success) return Output($"Invalid / Fragments  {match.Groups[1].Value}");
        match = LapSpreadPattern().Match(text);
        if (match.Success) return Output($"Fastest-to-slowest spread: {match.Groups[1].Value} s");
        var fragmentTranslation = TranslateChineseFragments(text);
        if (ContainsChinese().IsMatch(fragmentTranslation))
            return IsGerman ? "Nicht übersetzter Systemtext" : "Untranslated system text";
        return Output(fragmentTranslation);
    }

    private static string TranslateRemainder(string remainder)
    {
        if (!ContainsChinese().IsMatch(remainder)) return remainder;
        var translated = TranslateChineseFragments(remainder);
        return ContainsChinese().IsMatch(translated)
            ? (IsGerman ? "Nicht übersetzter Systemtext" : "Untranslated system text")
            : translated;
    }

    private static string Output(string english) => IsGerman ? Germanize(english) : english;

    private static string TranslateChineseFragments(string text)
    {
        foreach (var (chinese, english) in ChineseFragments)
            text = text.Replace(chinese, english, StringComparison.Ordinal);
        return text;
    }

    private static string Germanize(string english)
    {
        if (GermanPhrases.TryGetValue(english, out var translated)) return translated;
        foreach (var (englishPart, germanPart) in GermanFragments)
            english = english.Replace(englishPart, germanPart, StringComparison.OrdinalIgnoreCase);
        return english;
    }

    [GeneratedRegex("[\\p{IsCJKUnifiedIdeographs}]")] private static partial Regex ContainsChinese();

    private static readonly (string Chinese, string English)[] Prefixes =
    [
        ("初始化失败：", "Initialization failed: "), ("打开记录失败：", "Failed to open session: "),
        ("导入失败：", "Import failed: "), ("恢复临时记录失败：", "Failed to recover temporary recording: "),
        ("结束记录时发生错误：", "An error occurred while ending the recording: "), ("无法开始实时采集：", "Unable to start live capture: "),
        ("实时采集发生错误：", "Live capture error: "), ("已保存到本地记录库：", "Saved to the local session library: "),
        ("已导出记录：", "Session exported: "), ("已导出：", "Exported: "), ("已恢复：", "Recovered: "),
        ("正在通过只读 LMU_Data 采集：", "Capturing through read-only LMU_Data: "),
        ("最大速度：", "Maximum speed: "), ("置信度 ", "Confidence ")
    ];

    // These fragments cover runtime diagnostics, recommendations, and data-grid values that are composed outside XAML.
    private static readonly (string Chinese, string English)[] ChineseFragments =
    [
        ("完整有效圈", "complete valid laps"), ("完整圈", "complete laps"), ("有效圈", "valid laps"), ("无效/片段", "invalid / fragments"), ("无效", "invalid"), ("有效", "valid"),
        ("实时采集", "live capture"), ("单圈回放", "lap replay"), ("双圈对比", "lap comparison"), ("多圈总览", "multi-lap overview"), ("调教建议", "setup advice"),
        ("当前圈", "current lap"), ("参考圈", "reference lap"), ("最快圈", "fastest lap"), ("最佳圈", "best lap"), ("圈速", "lap time"), ("圈", "lap"),
        ("油门", "throttle"), ("刹车", "brake"), ("弯心", "apex"), ("入弯", "turn-in"), ("出弯", "exit"), ("换挡", "gear shift"), ("进站", "pit entry"), ("出站", "pit exit"),
        ("赛道", "track"), ("车辆", "vehicle"), ("会话", "session"), ("记录", "recording"), ("数据", "data"), ("样本", "samples"), ("轨迹", "trace"),
        ("已连接", "connected"), ("未运行", "not running"), ("等待", "waiting"), ("正在", ""), ("失败", "failed"), ("错误", "error"), ("完成", "completed"),
        ("导入", "import"), ("导出", "export"), ("保存", "save"), ("恢复", "recover"), ("删除", "delete"), ("设置", "settings"), ("分析", "analysis"),
        ("只读", "read-only"), ("官方", "official"), ("建议", "advice"), ("证据", "evidence"), ("置信度", "confidence"), ("来源", "source"),
        ("晴天", "clear"), ("阴天", "cloudy"), ("雨天", "rain"), ("小雨", "light rain"), ("中雨", "moderate rain"), ("大雨", "heavy rain"),
        ("正赛", "race"), ("练习赛", "practice"), ("排位赛", "qualifying"), ("热身赛", "warmup"), ("测试日", "test day"), ("片段", "fragment"),
        ("前", "front"), ("后", "rear"), ("与", " and "), ("或", " or "), ("和", " and "), ("但", " but "), ("不", "not ")
    ];

    private static readonly Dictionary<string, string> GermanPhrases = new(StringComparer.Ordinal)
    {
        ["Settings"] = "Einstellungen", ["Display"] = "Anzeige", ["Language"] = "Sprache", ["Chinese"] = "Chinesisch", ["English"] = "Englisch", ["German"] = "Deutsch",
        ["Game Connection"] = "Spielverbindung", ["Data & Storage"] = "Daten & Speicher", ["About ApexTrace"] = "Über ApexTrace",
        ["Live Capture"] = "Live-Erfassung", ["Multi-Lap Overview"] = "Mehr-Runden-Übersicht", ["Lap Replay"] = "Rundenwiedergabe", ["Lap Comparison"] = "Rundenvergleich", ["Setup Advice"] = "Setup-Empfehlungen", ["Session Library"] = "Sitzungsbibliothek",
        ["Home"] = "Startseite", ["Connection Status"] = "Verbindungsstatus", ["Installation Folder"] = "Installationsordner", ["Choose Folder…"] = "Ordner wählen…",
        ["Official Telemetry Files"] = "Offizielle Telemetriedateien", ["Rescan and Import"] = "Erneut suchen und importieren", ["Safety Boundary"] = "Sicherheitsgrenze",
        ["Throttle / Brake Display"] = "Gas-/Bremsanzeige", ["Classic Bars"] = "Klassische Balken", ["Scrolling Graph"] = "Laufendes Diagramm",
        ["Saved Sessions"] = "Gespeicherte Sitzungen", ["Storage Used"] = "Belegter Speicher", ["Library Location"] = "Bibliotheksordner", ["Open in File Explorer"] = "Im Explorer öffnen", ["Manage Local Sessions"] = "Lokale Sitzungen verwalten",
        ["Refresh"] = "Aktualisieren", ["Date"] = "Datum", ["Track"] = "Strecke", ["Complete Laps"] = "Vollständige Runden", ["Samples"] = "Messwerte", ["Open Analysis"] = "Analyse öffnen", ["Export Copy"] = "Kopie exportieren", ["Move to Recycle Bin"] = "In Papierkorb verschieben",
        ["Current Track"] = "Aktuelle Strecke", ["Current Vehicle"] = "Aktuelles Fahrzeug", ["Data Source"] = "Datenquelle", ["Latest Import"] = "Letzter Import", ["Start Recording Guide"] = "Anleitung zum Aufzeichnen",
        ["Current Tires"] = "Aktuelle Reifen", ["Air Temperature"] = "Lufttemperatur", ["Track Temperature"] = "Streckentemperatur", ["Fuel"] = "Kraftstoff", ["Weather"] = "Wetter", ["Available"] = "Verfügbar", ["Unavailable"] = "Nicht verfügbar",
        ["Current"] = "Aktuell", ["Reference"] = "Referenz", ["Delta"] = "Differenz", ["Speed Comparison"] = "Geschwindigkeitsvergleich", ["Maximum Speed"] = "Höchstgeschwindigkeit", ["Pedal Input Comparison"] = "Pedalvergleich",
        ["Corner Analysis"] = "Kurvenanalyse", ["Evidence"] = "Nachweise", ["Read-only Detection"] = "Schreibgeschützte Erkennung", ["All"] = "Alle", ["Valid Laps"] = "Gültige Runden", ["Invalid Laps"] = "Ungültige Runden",
        ["Waiting for data"] = "Warte auf Daten", ["Waiting for telemetry"] = "Warte auf Telemetrie", ["Insufficient data"] = "Unzureichende Daten", ["Unknown vehicle"] = "Unbekanntes Fahrzeug",
        ["LMU Connected"] = "LMU verbunden", ["LMU Not Running"] = "LMU läuft nicht", ["LMU Detected / Waiting for Interface"] = "LMU erkannt / warte auf Schnittstelle",
        ["Clear"] = "Klar", ["Cloudy"] = "Bewölkt", ["Rain"] = "Regen", ["Light Rain"] = "Leichter Regen", ["Moderate Rain"] = "Mäßiger Regen", ["Heavy Rain"] = "Starker Regen",
        ["Race"] = "Rennen", ["Practice"] = "Training", ["Qualifying"] = "Qualifying", ["Warmup"] = "Warm-up", ["Test"] = "Test"
        , ["Display preferences are saved automatically and applied to live capture and lap replay. Lap comparison always uses the classic comparison style."] = "Anzeigeeinstellungen werden automatisch gespeichert und für Live-Erfassung und Rundenwiedergabe verwendet. Der Rundenvergleich nutzt immer den klassischen Vergleichsstil."
        , ["Choose the application language. The setting is saved automatically and applied immediately."] = "Wählen Sie die Sprache der Anwendung. Die Einstellung wird automatisch gespeichert und sofort angewendet."
        , ["Choose separate input bars or a throttle and brake graph that scrolls with playback. Lap comparison keeps its original display."] = "Wählen Sie separate Eingabebalken oder ein mit der Wiedergabe laufendes Gas- und Bremsdiagramm. Der Rundenvergleich behält seine ursprüngliche Darstellung."
        , ["Shows the current throttle and brake percentages in separate input bars."] = "Zeigt die aktuellen Gas- und Bremsprozente in separaten Eingabebalken."
        , ["Green throttle and red brake share a 0–100% vertical axis. The current point stays at the right edge while history scrolls left."] = "Grünes Gas und rote Bremse teilen sich eine vertikale Achse von 0–100 %. Der aktuelle Punkt bleibt am rechten Rand, während der Verlauf nach links läuft."
    };

    private static readonly (string English, string German)[] GermanFragments =
    [
        ("Settings", "Einstellungen"), ("Display", "Anzeige"), ("Language", "Sprache"), ("Connection", "Verbindung"), ("Session", "Sitzung"), ("Sessions", "Sitzungen"),
        ("Track", "Strecke"), ("Vehicle", "Fahrzeug"), ("Lap", "Runde"), ("Laps", "Runden"), ("Current", "Aktuell"), ("Reference", "Referenz"),
        ("Data", "Daten"), ("Storage", "Speicher"), ("Save", "Speichern"), ("Saved", "Gespeichert"), ("Import", "Importieren"), ("Export", "Exportieren"),
        ("Open", "Öffnen"), ("Close", "Schließen"), ("Delete", "Löschen"), ("Refresh", "Aktualisieren"), ("Start", "Starten"), ("End", "Beenden"),
        ("Replay", "Wiedergabe"), ("Comparison", "Vergleich"), ("Advice", "Empfehlungen"), ("Analysis", "Analyse"), ("Evidence", "Nachweise"),
        ("Throttle", "Gas"), ("Brake", "Bremse"), ("Speed", "Geschwindigkeit"), ("Fuel", "Kraftstoff"), ("Tires", "Reifen"), ("Weather", "Wetter"),
        ("Valid", "Gültig"), ("Invalid", "Ungültig"), ("Complete", "Vollständig"), ("Waiting", "Warte"), ("Connected", "Verbunden"), ("Not Running", "Läuft nicht"),
        ("read-only", "schreibgeschützt"), ("Read-only", "Schreibgeschützt"), ("official", "offiziell"), ("Official", "Offiziell"), ("and", "und"), ("or", "oder"), ("with", "mit"), ("to", "zu")
        , ("preferences", "Einstellungen"), ("Preferences", "Einstellungen"), ("are", "werden"), ("is", "ist"), ("saved", "gespeichert"), ("automatically", "automatisch"), ("applied", "angewendet"), ("live", "live"),
        ("capture", "Erfassung"), ("replay", "Wiedergabe"), ("comparison", "Vergleich"), ("always", "immer"), ("uses", "verwendet"), ("classic", "klassisch"), ("style", "Stil"),
        ("Choose", "Wählen Sie"), ("choose", "wählen"), ("application", "Anwendung"), ("setting", "Einstellung"), ("immediately", "sofort"), ("separate", "separate"), ("input", "Eingabe"),
        ("bars", "Balken"), ("graph", "Diagramm"), ("that", "das"), ("scrolls", "läuft"), ("playback", "Wiedergabe"), ("keeps", "behält"), ("its", "seine"), ("original", "ursprüngliche"),
        ("Shows", "Zeigt"), ("shows", "zeigt"), ("current", "aktuelle"), ("percentages", "Prozente"), ("in", "in"), ("Green", "Grünes"), ("green", "grünes"), ("red", "rote"),
        ("share", "teilen"), ("vertical", "vertikale"), ("axis", "Achse"), ("point", "Punkt"), ("stays", "bleibt"), ("right", "rechten"), ("edge", "Rand"), ("while", "während"), ("history", "Verlauf"), ("left", "links")
    ];

    [GeneratedRegex(@"^圈\s*(\d+)$")] private static partial Regex LapPattern();
    [GeneratedRegex(@"^版本\s+(.+)$")] private static partial Regex VersionPattern();
    [GeneratedRegex(@"^共\s*(\d+)\s*个会话$")] private static partial Regex SessionCountPattern();
    [GeneratedRegex(@"^([\d,]+)\s*样本$")] private static partial Regex SampleCountPattern();
    [GeneratedRegex(@"^(\d+)\s*个完整圈可回放$")] private static partial Regex CompleteLapReplayPattern();
    [GeneratedRegex(@"^当前：圈\s*(\d+)\s*·\s*([\d.]+)s$")] private static partial Regex CurrentLapPattern();
    [GeneratedRegex(@"^参考：圈\s*(\d+)\s*·\s*([\d.]+)s$")] private static partial Regex ReferenceLapPattern();
    [GeneratedRegex(@"^降水概率\s+(.+)$")] private static partial Regex ProbabilityPattern();
    [GeneratedRegex(@"^(正赛|练习赛|排位赛|热身赛|测试日)(\s*\d*)$")] private static partial Regex SessionTypePattern();
    [GeneratedRegex(@"^全部圈\s+(\d+)$")] private static partial Regex AllLapsPattern();
    [GeneratedRegex(@"^有效圈\s+(\d+)$")] private static partial Regex ValidLapsPattern();
    [GeneratedRegex(@"^无效/片段\s+(\d+)$")] private static partial Regex InvalidLapsPattern();
    [GeneratedRegex(@"^最快与最慢相差\s+([\d.]+)\s+s$")] private static partial Regex LapSpreadPattern();
}

public static class LocalizationBehavior
{
    private sealed class PropertyState
    {
        public string? Source { get; set; }
        public string? LastApplied { get; set; }
        public bool Updating { get; set; }
    }

    private sealed class WindowState
    {
        public HashSet<DependencyObject> Seen { get; } = [];
        public Dictionary<(DependencyObject Target, DependencyProperty Property), PropertyState> Properties { get; } = [];
    }

    private static readonly Dictionary<Window, WindowState> Windows = [];

    public static void Enable(Window window)
    {
        var state = new WindowState();
        Windows[window] = state;
        void Refresh(object? _, EventArgs __) => window.Dispatcher.BeginInvoke(() => RefreshTracked(state));
        var discoveryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Scan(window, state), window.Dispatcher);
        LocalizationManager.LanguageChanged += Refresh;
        discoveryTimer.Start();
        window.Closed += (_, _) =>
        {
            LocalizationManager.LanguageChanged -= Refresh;
            discoveryTimer.Stop();
            Windows.Remove(window);
        };
        Scan(window, state);
    }

    public static void Refresh(Window window)
    {
        if (Windows.TryGetValue(window, out var state)) Scan(window, state);
    }

    private static void RefreshTracked(WindowState state)
    {
        foreach (var ((target, property), propertyState) in state.Properties.ToArray())
            Apply(target, property, propertyState);
    }

    private static void Scan(DependencyObject root, WindowState state)
    {
        Visit(root, state);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++) Scan(VisualTreeHelper.GetChild(root, index), state);
    }

    private static void Visit(DependencyObject target, WindowState state)
    {
        if (!state.Seen.Add(target)) return;
        if (target is TextBlock) Track(target, TextBlock.TextProperty, state);
        if (target is ContentControl) Track(target, ContentControl.ContentProperty, state);
        if (target is HeaderedContentControl) Track(target, HeaderedContentControl.HeaderProperty, state);
        if (target is Window) Track(target, Window.TitleProperty, state);
        Track(target, ToolTipService.ToolTipProperty, state);
    }

    private static void Track(DependencyObject target, DependencyProperty property, WindowState windowState)
    {
        if (windowState.Properties.ContainsKey((target, property))) return;
        var state = new PropertyState();
        windowState.Properties[(target, property)] = state;
        var descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
        descriptor?.AddValueChanged(target, (_, _) => Apply(target, property, state));
        Apply(target, property, state);
    }

    private static void Apply(DependencyObject target, DependencyProperty property, PropertyState state)
    {
        if (state.Updating || target.GetValue(property) is not string current) return;
        state.Updating = true;
        try
        {
            // A binding update is a new source value. A value written by this behavior is not.
            if (state.Source is null || !string.Equals(current, state.LastApplied, StringComparison.Ordinal))
            {
                state.Source = current;
            }

            var desired = LocalizationManager.CurrentLanguage == LocalizationManager.ChineseLanguage
                ? state.Source
                : LocalizationManager.Translate(state.Source);
            state.LastApplied = desired;
            if (!string.Equals(desired, current, StringComparison.Ordinal))
            {
                target.SetCurrentValue(property, desired);
            }
        }
        finally
        {
            state.Updating = false;
        }
    }
}
