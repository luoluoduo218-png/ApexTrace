# ApexTrace：Codex 完整开发说明书

**目标平台：** Windows 10/11 x64  
**目标游戏：** Le Mans Ultimate（LMU）  
**目标框架：** .NET 10 LTS + WPF  
**文档版本：** 1.0（2026-07-18）

---

## 0. 给 Codex 的总指令

你要实现的是一个可实际使用的软件，而不是概念验证、静态原型或只有漂亮界面的 Demo。

必须完整交付：

- LMU 进程发现与连接状态；
- 官方共享内存读取与结构版本校验；
- “开始记录 / 结束记录”完整状态机；
- 实时赛道位置、速度、挡位、RPM、油门、刹车、方向、G 值、轮胎、燃油等显示；
- 临时记录、崩溃恢复、保存、导出和丢弃；
- 多圈总览、单圈回放、双圈比较；
- 赛道解析与坐标拟合；
- 逐弯分析、目标走线、驾驶建议；
- 基于规则和证据的基础调教建议；
- 本地记录库和设置页；
- 自动化测试、日志、诊断页和发布构建。

### 工作纪律

1. 先创建完整解决方案和模块边界，再写功能。
2. 每个外部数据结构必须有单元测试和二进制快照测试。
3. 每个页面先用设计时模拟数据完成，然后接真实数据。
4. 不得把所有代码放进 `MainWindow.xaml.cs`。
5. 不得依赖旧的 `rFactor2SharedMemoryMapPlugin64.dll`。
6. 不得向 LMU 进程注入 DLL，不得写游戏内存，不得绕过 EAC。
7. 不得破解加密 MAS；解析失败必须明确降级并显示数据来源。
8. 不得复制 GPL 项目 TinyPedal 的业务代码。可以观察行为和字段；共享内存映射优先以游戏本地官方 Header 为唯一真源。
9. `pyLMUSharedMemory` 是 MIT 许可，可作为字段映射交叉验证，但仍需保留许可声明，并以本地 Header 重新生成/核对 C# 结构。
10. 每完成一个阶段：运行 `dotnet test`、启动应用、生成页面截图、检查日志、更新 `docs/ACCEPTANCE_CHECKLIST.md`、提交 Git。

---

## 1. 产品范围和用户流程

### 1.1 核心用户流程

```text
启动 ApexTrace
  -> 自动寻找 LMU
  -> 显示赛道、赛车、连接与解析状态
  -> 用户点击“开始记录”
  -> 创建临时会话并进入实时采集页
  -> 连续采集、实时显示、自动分圈
  -> 用户点击“结束记录”
  -> 停止采集并完成数据校验与分析
  -> 弹出：保存到本地 / 导出文件 / 丢弃记录
```

### 1.2 应用状态机

```text
Disconnected
  -> GameDetected
  -> SharedMemoryReady
  -> TrackAndVehicleReady
  -> ReadyToRecord
  -> Recording
  -> Finalizing
  -> ReviewPending
  -> Saved | Exported | Discarded
```

任何异常都进入 `Faulted`，但必须保留临时记录并提供“恢复会话”。

### 1.3 第一版明确不做

- 不做多人云端排行榜；
- 不做手机端；
- 不自动向 LMU 写入调教；
- 不声称计算出绝对“完美路线”；
- 不对加密内容做破解；
- 不使用机器学习训练驾驶模型。

代码结构必须为未来的跨设备传输、多人分享和移动端预留接口，但不要提前做复杂服务器。

---

## 2. 技术架构

### 2.1 技术选型

| 领域 | 选择 | 用途 |
|---|---|---|
| 框架 | .NET 10 LTS | Windows 桌面与长期支持 |
| UI | WPF + XAML | 原生桌面窗口、数据绑定、可访问性 |
| MVVM | CommunityToolkit.Mvvm | Observable、Command、消息 |
| 赛道绘制 | SkiaSharp.Views.WPF | 高性能路径、渐变线、缩放和平移 |
| 图表 | ScottPlot.WPF | 速度、踏板、Delta、分布图 |
| 数据库 | DuckDB.NET.Data.Full | 会话、圈、分析结果和查询 |
| 原始遥测 | Parquet.Net | 紧凑列式存储，便于 AI 和外部分析 |
| 日志 | Serilog + File Sink | 结构化日志、滚动文件 |
| 宿主/DI | Microsoft.Extensions.Hosting | 生命周期、依赖注入、配置 |
| 测试 | xUnit + FluentAssertions | 单元与集成测试 |
| UI 自动化 | FlaUI.UIA3 | 页面导航和核心交互测试 |

不要同时引入第二套 MVVM、第二套图表库或大型主题框架。视觉样式用项目自己的 `ResourceDictionary` 实现。

### 2.2 解决方案结构

```text
ApexTrace/
├─ ApexTrace.sln
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ src/
│  ├─ ApexTrace.App/                 # WPF 启动、页面、资源、导航
│  ├─ ApexTrace.Core/                # 领域模型、接口、规则
│  ├─ ApexTrace.Lmu/                 # 进程发现、官方共享内存、Header 适配
│  ├─ ApexTrace.Recording/           # 缓冲、分圈、临时记录、恢复
│  ├─ ApexTrace.Track/               # 赛道发现、AIW/GDB/MAS/GMT 解析、拟合
│  ├─ ApexTrace.Analysis/            # 对齐、弯道、Delta、目标线、建议
│  ├─ ApexTrace.Setup/               # 调教快照、规则与方案
│  ├─ ApexTrace.Storage/             # DuckDB、Parquet、.apextrace 包
│  └─ ApexTrace.Rendering/           # Skia 赛道画布、颜色映射、命中测试
├─ tests/
│  ├─ ApexTrace.Core.Tests/
│  ├─ ApexTrace.Lmu.Tests/
│  ├─ ApexTrace.Recording.Tests/
│  ├─ ApexTrace.Track.Tests/
│  ├─ ApexTrace.Analysis.Tests/
│  ├─ ApexTrace.Storage.Tests/
│  └─ ApexTrace.Ui.Tests/
├─ fixtures/
│  ├─ shared-memory/
│  ├─ telemetry/
│  ├─ tracks/
│  └─ sessions/
├─ assets/ui/                         # 本开发包中的参考图
├─ docs/
└─ scripts/
   ├─ bootstrap.ps1
   ├─ run-dev.ps1
   ├─ build-release.ps1
   ├─ capture-ui.ps1
   └─ verify-package.ps1
```

### 2.3 项目依赖方向

```text
App -> Core, Lmu, Recording, Track, Analysis, Setup, Storage, Rendering
Lmu/Recording/Track/Analysis/Setup/Storage/Rendering -> Core
Core -> 不依赖任何其他项目
```

禁止循环依赖。

---

## 3. 初始化命令

```powershell
mkdir ApexTrace
cd ApexTrace
git init

dotnet new sln -n ApexTrace

dotnet new wpf     -n ApexTrace.App       -o src/ApexTrace.App       -f net10.0-windows
dotnet new classlib -n ApexTrace.Core      -o src/ApexTrace.Core      -f net10.0
dotnet new classlib -n ApexTrace.Lmu       -o src/ApexTrace.Lmu       -f net10.0-windows
dotnet new classlib -n ApexTrace.Recording -o src/ApexTrace.Recording -f net10.0
dotnet new classlib -n ApexTrace.Track     -o src/ApexTrace.Track     -f net10.0
dotnet new classlib -n ApexTrace.Analysis  -o src/ApexTrace.Analysis  -f net10.0
dotnet new classlib -n ApexTrace.Setup     -o src/ApexTrace.Setup     -f net10.0
dotnet new classlib -n ApexTrace.Storage   -o src/ApexTrace.Storage   -f net10.0
dotnet new classlib -n ApexTrace.Rendering -o src/ApexTrace.Rendering -f net10.0-windows
```

添加所有项目到解决方案，并建立依赖。然后添加包：

```powershell
dotnet add src/ApexTrace.App package CommunityToolkit.Mvvm
dotnet add src/ApexTrace.App package Microsoft.Extensions.Hosting
dotnet add src/ApexTrace.App package Serilog.Extensions.Hosting
dotnet add src/ApexTrace.App package Serilog.Sinks.File
dotnet add src/ApexTrace.Rendering package SkiaSharp.Views.WPF
dotnet add src/ApexTrace.App package ScottPlot.WPF
dotnet add src/ApexTrace.Storage package DuckDB.NET.Data.Full
dotnet add src/ApexTrace.Storage package Parquet.Net
```

测试项目使用 xUnit、FluentAssertions；UI 测试使用 FlaUI。初始化后创建 `packages.lock.json` 并提交，避免依赖漂移。

---

## 4. LMU 数据接入：只用官方内置共享内存

### 4.1 事实与路径

LMU 在 1.2 Update 2 中加入了新的 Shared Memory Header，游戏安装目录的 `Support\SharedMemoryInterface` 是结构定义的最终真源。Windows 下内置 API 不需要旧的 rFactor 2 共享内存 DLL，但游戏内 `Settings -> Gameplay -> Enable Plugins` 必须开启。

共享内存名称：

```text
LMU_Data
```

C# 使用：

```csharp
MemoryMappedFile.OpenExisting(
    "LMU_Data",
    MemoryMappedFileRights.Read);
```

应用必须以 x64 编译，并使用只读视图。

### 4.2 启动时定位 LMU

按以下顺序定位：

1. 查找进程名和窗口标题包含 `Le Mans Ultimate` 的进程；
2. 从 Steam 注册表与 `libraryfolders.vdf` 定位游戏根目录；
3. 验证 `Support\SharedMemoryInterface` 是否存在；
4. 读取 Header 文件，计算 SHA-256；
5. 把 Header 哈希与应用支持的映射版本比较；
6. 未识别版本时进入“接口版本不匹配”状态，允许用户导出诊断，不允许盲读。

### 4.3 C# 结构映射规则

所有结构：

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
```

C++ `bool` 必须按 1 字节映射：

```csharp
[MarshalAs(UnmanagedType.I1)]
public bool PlayerHasVehicle;
```

固定字符数组按 Header 长度定义；不要用默认 C# `bool` 和自动布局。所有结构必须有 `Marshal.SizeOf<T>()` 测试，并与 Header 生成的预期尺寸匹配。

### 4.4 根结构

当前参考映射的根结构为：

```text
SharedMemoryObjectOut
├─ generic
│  ├─ events
│  ├─ gameVersion
│  ├─ FFBTorque
│  └─ appInfo
├─ paths
│  ├─ userData
│  ├─ customVariables
│  ├─ stewardResults
│  ├─ playerProfile
│  └─ pluginsFolder
├─ scoring
│  ├─ scoringInfo
│  ├─ vehScoringInfo[104]
│  └─ scoringStream
└─ telemetry
   ├─ activeVehicles
   ├─ playerVehicleIdx
   ├─ playerHasVehicle
   └─ telemInfo[104]
```

优先使用 `telemetry.playerVehicleIdx`；同时用 scoring 中 `mIsPlayer` 和 `mControl == 0` 交叉验证。

### 4.5 必采字段

#### 会话与赛道

- track name, session type, game phase；
- current elapsed time, remaining time, max laps；
- track length, current lap distance；
- ambient/track temperature, rain, wetness, wind, grip level；
- fixed setup flag、服务器/在线状态。

#### 玩家与圈速

- player vehicle index 和 ID；
- lap number, lap start time, current sector；
- best/last/current sector time；
- best/last lap time；
- lap invalidated；
- path lateral、track edge；
- world position、orientation、local velocity/acceleration/rotation。

#### 驾驶输入与动力

- unfiltered throttle/brake/steering/clutch；
- filtered values（仅用于比较，不覆盖原始输入）；
- speed、gear、engine RPM、torque；
- fuel、fuel capacity；
- brake bias、TC、ABS、TC Slip/Cut、motor map；
- hybrid SOC、regen、virtual energy、boost motor state。

#### 四轮

- suspension deflection、ride height、suspension force；
- brake temperature/pressure；
- rotation；
- lateral/longitudinal patch velocity；
- lateral/longitudinal force；
- tire load、grip fraction、pressure；
- surface/carcass/inner temperatures；
- wear、surface type、flat/detached、camber/toe。

### 4.6 读取循环与一致性

不要在 UI 线程读共享内存。

建议服务：

```text
LmuProcessWatcher        1 Hz
LmuSharedMemoryReader    100-400 Hz，只有事件计数变化才发布
TelemetryNormalizer      独立后台线程
SessionRecorder          批量写入
UiTelemetryAggregator    60 Hz，只发布最新状态
```

共享内存可能在复制时被游戏更新。采用“双读事件计数”策略：

1. 读 `SME_UPDATE_TELEMETRY` 和 `SME_UPDATE_SCORING`；
2. 把整个根结构复制到预分配字节缓冲；
3. 再读两项事件计数；
4. 计数相同则接受；不同则重试，最多 3 次；
5. 连续失败记录警告，但不阻塞游戏。

使用 `Channel<T>` 或无锁环形缓冲。UI 通道可丢弃旧帧，只保留最新帧；记录通道不得静默丢失数据。若写盘追不上，显示“记录器积压”告警并扩大批量，而不是丢样本。

### 4.7 原生 DuckDB 遥测作为导入源

LMU 1.2 起可以在游戏内自动或热键记录 DuckDB，配置文件位于 `UserData/Telemetry/config.json`。ApexTrace 要实现 `LmuDuckDbImporter`：

- 扫描新增的 DuckDB 文件；
- 读取可用 channel/event 列；
- 映射到统一 `TelemetrySample`；
- 用于离线导入、接口对照和共享内存故障恢复；
- 不把游戏原生记录当作实时 UI 的唯一来源。

### 4.8 插件与反作弊规则

- 第一版不创建游戏内 DLL 插件；官方共享内存已经足够。
- 不使用旧 rFactor 2 Shared Memory DLL。
- 不向 `Bin64\Plugins` 放任何自制 DLL。
- 不读取受保护进程内存，不使用注入、Hook 或驱动。
- LMU 更新后先检查官方 Header 和已知问题。
- 如果用户遇到崩溃，提供按钮打开 `CustomPluginVariables.JSON` 所在目录，但不要自动修改其他插件。

---

## 5. 领域数据契约

### 5.1 TelemetrySample

```csharp
public sealed record TelemetrySample(
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    double SessionElapsedSeconds,
    int LapNumber,
    double LapDistanceMeters,
    Vector3D WorldPosition,
    Orientation3D Orientation,
    Vector3D LocalVelocity,
    Vector3D LocalAcceleration,
    double SpeedMetersPerSecond,
    int Gear,
    double EngineRpm,
    double Throttle,
    double Brake,
    double Steering,
    double Clutch,
    double FuelLiters,
    double BrakeBiasRear,
    bool AbsActive,
    bool TcActive,
    VehicleControlSettings Controls,
    WheelSample[] Wheels,
    EnvironmentSample Environment,
    SampleQuality Quality);
```

所有 0-1 输入在领域层保持 0-1，不要提前变成百分数。显示层再乘 100。

### 5.2 会话数据

```text
SessionMetadata
LapRecord
TelemetrySample
TrackDefinition
VehicleSnapshot
SetupSnapshot
DrivingEvent
CornerDefinition
CornerPerformance
TargetLine
Recommendation
```

每种数据必须有 `SchemaVersion`。导出和数据库迁移都基于版本号。

### 5.3 DrivingEvent 类型

- LapStarted / LapCompleted；
- BrakeStarted / PeakBrake / BrakeReleased；
- TurnIn；
- Apex；
- ThrottleStarted / FullThrottle；
- GearShift；
- ABSActivation / TCActivation；
- OffTrack / LapInvalidated；
- PitEntry / PitExit；
- Impact。

---

## 6. 开始与结束记录

### 6.1 开始按钮前置条件

只有同时满足以下条件按钮才可用：

- LMU 进程存在；
- `LMU_Data` 打开成功；
- 玩家拥有车辆；
- 处于实时驾驶或允许的赛道状态；
- track name 和 vehicle name 非空；
- 临时目录可写；
- 可用磁盘空间大于安全阈值。

### 6.2 点击“开始记录”

1. 固化会话元数据；
2. 创建 `SessionId`；
3. 创建 `Temp/<SessionId>/`；
4. 写入 `manifest.partial.json`；
5. 保存 Header 哈希和应用版本；
6. 开始记录原始样本；
7. 采集当前调教快照；
8. 初始化分圈器和赛道解析器；
9. 导航到实时采集页；
10. 每 30 秒写恢复检查点。

### 6.3 点击“结束记录”

1. 停止接收新样本；
2. 排空记录队列；
3. 完成最后一圈或标记不完整圈；
4. 校验时间戳、序号、样本缺口；
5. 完成 Parquet 与数据库写入；
6. 运行第一轮分析；
7. 生成赛道预览 SVG/PNG；
8. 进入 `ReviewPending`；
9. 弹出结束记录对话框。

### 6.4 三个动作

- **保存到本地：** 移入本地记录库，并加入 DuckDB 索引；
- **导出文件：** 生成单个 `.apextrace` 包，用户选择路径；
- **丢弃记录：** 二次确认后删除临时目录。

关闭对话框不等于丢弃；未处理记录保持在恢复区。

---

## 7. 数据保存和 `.apextrace` 格式

`.apextrace` 是 ZIP 容器：

```text
manifest.json
session.json
track/track.json
track/track.svg
vehicle/vehicle.json
setup/setup.json
telemetry/samples.parquet
laps/laps.csv
corners/corners.csv
analysis/summary.json
analysis/recommendations.json
analysis/target-line.json
preview/session.png
checksums.sha256
```

`manifest.json` 至少包含：

- package schema version；
- app version；
- LMU game version；
- shared-memory Header SHA-256；
- track、vehicle、session；
- sample count、sample rate statistics；
- start/end UTC；
- data source；
- 文件清单与校验和。

AI 友好导出必须包含 CSV/JSON 摘要，不能只给二进制数据库。

### 7.1 本地数据库

DuckDB 表：

```text
sessions
laps
sectors
corners
corner_performance
recommendations
setup_snapshots
session_tags
files
```

高频样本保存在 Parquet，通过路径和 SessionId 关联，避免把数百万行全部塞进 UI 数据库。

---

## 8. 赛道发现、解析和精准拟合

### 8.1 数据来源优先级

```text
1. LMU 本地官方 Header + 当前世界坐标
2. 可合法读取的 AIW/GDB/SCN/TDF/GMT
3. 游戏原生遥测的圈内距离和位置
4. 多圈轨迹重建边界
```

每个 `TrackDefinition` 必须保存：

```text
Source = OfficialAIW | OfficialGeometry | RuntimeReconstruction
AccuracyEstimateMeters
SourceFilesHash
TrackVersion
```

### 8.2 赛道文件职责

- `GDB`：布局名称、长度、位置等元数据；
- `AIW`：AI waypoint、中心路径与走廊，是 2D 赛道几何的首选；
- `SCN`：场景对象和资源搜索路径；
- `TDF`：表面材质物理定义；
- `GMT`：网格；
- `MAS`：内容归档；
- `MFT`：安装组件版本与校验信息。

正式内容的 MAS 可能加密。不得破解。解析器必须支持：

- 未加密/可访问内容：直接解析；
- 官方工具可正常导出的内容：通过用户明确选择导入；
- 加密内容：标记 `ProtectedContent`，切换运行时重建。

### 8.3 安装目录发现

- 从 Steam Library 查根目录；
- 不硬编码 C 盘；
- 扫描 `Installed`、`UserData` 和 Header 中 `paths`；
- 当前 track name 用 scoring 数据确认；
- 通过 MFT 和文件哈希区分同名不同版本布局。

### 8.4 坐标系统

遥测 `mPos` 是世界坐标（米），显示平面默认使用：

```text
Screen X <- World X
Screen Y <- -World Z
Elevation <- World Y
```

如果 AIW/GMT 使用相同坐标系，直接映射；不要凭视觉旋转拟合。仅当实测坐标系不同才使用刚体变换：

```text
p_screen = scale * R * p_world + translation
```

不允许非均匀缩放。

### 8.5 拟合流程

1. 解析中心线和左右走廊；
2. 计算弧长参数 `s`；
3. 将每个遥测点投影到最近中心线段；
4. 用 `mLapDist` 选择正确的局部最邻近，避免赛道交叉处误匹配；
5. 计算横向偏移；
6. 用 `mPathLateral` 和 `mTrackEdge` 进行交叉校验；
7. 记录残差分布；
8. 残差超阈值时不显示“精准”，而显示诊断。

### 8.6 第一版验收精度

- 中位横向投影误差 <= 0.10 m；
- 95 分位误差 <= 0.25 m；
- 起终点闭合误差 <= 0.20 m；
- 圈内距离对齐误差 <= 0.25 m；
- 数据来源和误差在设置/诊断页可见。

运行时重建达不到该标准时允许功能继续，但 UI 必须显示精度等级。

---

## 9. 分圈、重采样和回放

### 9.1 分圈

同时使用：

- `mLapNumber` 变化；
- `mLapDist` 从终点回绕；
- `mLapStartET` 变化；
- scoring 的 last lap time。

只有至少两个信号一致才确认新圈，防止进站和重置造成假圈。

### 9.2 距离重采样

分析统一按赛道距离，不按采集时间直接比较。

默认网格：

```text
0.5 m 一个点
```

连续量：单调三次插值或线性插值；离散量：最近邻/保持；事件：保持原始距离。

### 9.3 回放

- 原始时间戳驱动车辆动画；
- UI 60 FPS 插值；
- 0.25x、0.5x、1x、2x；
- 支持逐帧；
- 时间轴上显示刹车、弯心、补油和失效事件；
- 点击赛道线或时间轴必须双向联动。

---

## 10. 多圈、双圈和 Delta

### 10.1 多圈总览

- 当前选中圈：高亮蓝；
- 最快圈：亮蓝加粗；
- 其他有效圈：低透明蓝；
- 无效圈：灰色虚线；
- 目标线：红绿渐变，可开关；
- 大量圈数使用几何缓存，不每帧重建 Path。

### 10.2 双圈比较

按统一距离网格计算：

- 时间差；
- 速度差；
- 刹车点差；
- 补油/全油门点差；
- 最低速度和出弯速度；
- 每弯得失时间。

时间 Delta 的正负定义必须全应用一致：

```text
正值 = 当前圈更慢
负值 = 当前圈更快
```

---

## 11. 弯道和驾驶事件检测

### 11.1 弯道分段

融合：

- 中心线曲率；
- 方向盘输入；
- 横向 G；
- 速度变化；
- 已知赛道 AIW waypoint。

生成：

```text
CornerEntry
BrakingZone
TurnIn
ApexRegion
Exit
FullThrottleZone
```

### 11.2 事件阈值初值

阈值必须可配置并按噪声自适应：

- BrakeStarted：刹车从 <2% 上穿 5%；
- PeakBrake：局部峰值；
- BrakeReleased：降到 <2%；
- ThrottleStarted：油门上穿 5%；
- FullThrottle：油门 >=95% 持续一定距离；
- TurnIn：方向输入显著上升且曲率方向一致；
- Apex：局部最小速度、最大曲率和横向位置联合估计。

### 11.3 规则建议

必须先由确定性算法提取证据，再生成文字：

- 提前刹车但弯心速度无收益；
- 刹车过晚导致最低速度低和出弯慢；
- 无效滑行过长；
- 松刹过快；
- 拖刹过多；
- 补油过晚；
- 油门过猛导致 TC 长介入；
- 方向修正次数过多；
- 入弯快但出弯损失更大；
- 路线不稳定。

每条建议必须有：证据、具体距离/百分比、预计收益区间、置信度、验证方法。

---

## 12. 目标走线

### 12.1 第一层：个人参考目标线

从相近条件下的有效圈构建：

- 选择每个弯表现最佳的连续片段；
- 检查片段连接的速度、位置和方向连续性；
- 不允许把互不兼容的片段机械拼接；
- 使用平滑和赛道边界约束。

### 12.2 第二层：物理约束优化

根据用户实测能力包络估计：

- 速度相关最大制动；
- 速度相关最大横向 G；
- 摩擦圆；
- 加速能力；
- TC/ABS 介入边界；
- 赛道左右边界。

优化目标是估计最低圈时，不是宣称绝对完美。数据不足时只显示参考线。

### 12.3 颜色规则

- Brake > 2%：红色；刹车越深，红色越深；
- Throttle > 2%：绿色；油门越深，绿色越深；
- 两者都低：黄色/橙色；
- 实际走线始终蓝色；
- 颜色计算按轨迹段，不按单个像素。

---

## 13. 调教快照和建议

### 13.1 可实时读取

优先保存共享内存暴露的：

- brake bias；
- TC、TC Slip、TC Cut；
- ABS；
- motor map、migration、regen；
- front/rear anti-roll bar；
- tire compound；
- wing/ride height 等可用状态。

### 13.2 本地调教文件

监控：

```text
Le Mans Ultimate\UserData\player\Settings\<trackname>
```

`.svm` 文件的具体字段不能假设。先建立 `SetupFileProbe`：

- 只读；
- 保存原文件哈希；
- 识别已知键值；
- 未知字段原样保留；
- 不覆盖用户文件；
- 导出建议为独立方案，只有用户明确操作后才生成副本。

### 13.3 建议原则

- 驾驶问题与调教问题分离；
- 先排除驾驶输入导致的问题；
- 每轮默认只推荐一个主要改动；
- 显示可能收益和副作用；
- 要求相近燃油、轮胎、天气下至少 3 个有效圈验证；
- 固定调教服务器不提供不可执行的改动。

---

## 14. UI 实现规范

全部页面视觉参考位于 `assets/ui/`。图片仅作视觉和布局规范，必须重建为真实控件。

### 14.1 全局设计令牌

```text
Background         #07111D
Surface            #0B1724
SurfaceRaised      #0F1E2E
Border             #233243
PrimaryBlue        #1677FF
BlueHighlight      #2B8CFF
SuccessGreen       #20C76F
WarningAmber       #F5B942
DangerRed          #F04444
TextPrimary        #F4F7FB
TextSecondary      #9AA8B7
ActualLine         #2B8CFF
ReferenceLine      #FF9D2E
```

建议字体：Segoe UI Variable / Microsoft YaHei UI。支持 Windows 缩放 100%-200%。窗口最小 1280x720，设计基准 1672x941，常规目标 1600x900。

### 14.2 应用壳

- 左侧固定导航；
- 顶部状态栏显示赛道、赛车、会话、当前圈、天气、连接；
- 开始/结束按钮在相关页面保持一致；
- 录制中使用红色状态块和计时；
- 页面切换不销毁全局会话服务；
- 遥测更新只更新必要属性，避免整个 Visual Tree 重建。

### 14.3 页面与图片

| 页面 | 图片 |
|---|---|
| 实时采集 | `assets/ui/01_realtime_capture.png` |
| 首页/就绪 | `assets/ui/02_home_ready.png` |
| 多圈总览 | `assets/ui/03_multi_lap_overview.png` |
| 单圈回放 | `assets/ui/04_single_lap_replay.png` |
| 双圈对比 | `assets/ui/05_two_lap_compare.png` |
| 弯道分析 | `assets/ui/06_corner_analysis.png` |
| 调教建议 | `assets/ui/07_setup_recommendations.png` |
| 结束记录 | `assets/ui/08_end_session_modal.png` |
| 记录库 | `assets/ui/09_session_library.png` |
| 设置 | `assets/ui/10_settings.png` |

详细控件要求见 `docs/UI_IMPLEMENTATION.md`。

### 14.4 赛道画布

实现 `TrackCanvasControl`：

- SkiaSharp 自定义绘制；
- 鼠标滚轮缩放；
- 中键/左键拖拽平移；
- 双击适应窗口；
- 自动跟随赛车；
- 轨迹命中测试；
- 事件点 Tooltip；
- Geometry 缓存；
- 支持卫星/纯色背景模式；
- 支持导出 SVG 和 PNG。

### 14.5 UI 回归

用设计时数据打开全部页面，固定 1672x941 截图。与参考图人工并排检查：

- 信息层级；
- 网格比例；
- 边距；
- 字号；
- 状态颜色；
- 控件是否被裁剪；
- 125%/150% DPI。

不要追求逐像素抄图，但页面结构和视觉语言必须一致。

---

## 15. 性能、可靠性和安全

### 15.1 性能目标

- 实时 UI 60 FPS；
- 共享内存到 UI 中位延迟 <= 50 ms；
- 一小时记录数据缺失率 <= 0.1%；
- 常规运行 CPU 平均 <= 8%（不含 LMU）；
- 记录进程内存稳定，不随圈数线性失控；
- 打开 100 圈记录库仍可流畅滚动；
- 赛道缩放和平移无明显卡顿。

### 15.2 崩溃恢复

- 每 30 秒写 checkpoint；
- manifest 使用原子替换；
- 启动扫描未完成会话；
- 提供恢复、导出诊断、删除；
- 不因单个损坏会话阻止应用启动。

### 15.3 日志

结构化字段：

```text
SessionId, GameVersion, HeaderHash, Track, Vehicle,
Sequence, LapNumber, QueueDepth, DroppedUiFrames,
RecorderBacklog, ParserSource, AccuracyEstimate
```

默认保留 14 天滚动日志；诊断包不得包含不必要的账号隐私信息。

---

## 16. 测试策略

### 16.1 共享内存结构测试

- 每个 Struct Size；
- 字段 Offset；
- bool 大小；
- fixed array 长度；
- Header 哈希映射；
- 使用保存的共享内存快照反序列化。

### 16.2 分圈测试

覆盖：

- 正常完成圈；
- 进站圈；
- 出站圈；
- ESC 回车库；
- 会话重启；
- 圈失效；
- 暂停；
- 回放模式；
- 时间/距离回绕。

### 16.3 分析测试

使用合成圈：

- 明确提前 10m 刹车；
- 补油晚 8m；
- 无效滑行；
- TC 介入；
- 走线偏移；
- 验证建议数值和正负方向。

### 16.4 UI 测试

- 所有导航项可打开；
- 开始按钮状态正确；
- 结束后弹出三选项；
- 丢弃有二次确认；
- 回放和赛道点击联动；
- 设置保存后重启仍生效；
- DPI 和最小窗口不裁剪。

---

## 17. 实施阶段

### 阶段 A：完整 UI 壳和模拟数据

交付：10 个页面、导航、主题、设计时模拟器、画布交互、结束记录对话框。

验收：所有 UI 图都有对应页面；应用可完整演示用户流程。

### 阶段 B：LMU 真实数据接入和记录

交付：进程发现、Header 验证、`LMU_Data`、实时仪表、分圈、临时记录、保存/丢弃、诊断。

验收：LMU 中连续记录 60 分钟不崩溃，导出可重新打开。

### 阶段 C：赛道解析和回放比较

交付：赛道发现、可访问 AIW/GDB 等解析、运行时重建、精度报告、多圈、单圈、双圈。

验收：实际轨迹和赛道位置符合误差标准；回放同步。

### 阶段 D：分析、目标线和调教建议

交付：弯道、事件、Delta、驾驶建议、目标线、基础调教建议、AI 包。

验收：所有建议可追溯到数据；数据不足不生成。

### 阶段 E：发布

交付：自包含 x64 Release、安装包、卸载、日志路径、版本更新说明、用户指南。

---

## 18. 发布和安装

```powershell
dotnet publish src/ApexTrace.App/ApexTrace.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:PublishReadyToRun=true
```

优先发布为文件夹 + 安装器，不要一开始强制 SingleFile，因为 Skia/DuckDB 原生库更容易在普通自包含发布中诊断。

首次启动向导：

1. 定位 LMU；
2. 检查 Enable Plugins；
3. 检查 Header；
4. 测试共享内存；
5. 选择存储目录；
6. 显示隐私和只读说明。

---

## 19. 必须保留的未来接口

- `ITelemetrySource`：未来其他模拟器；
- `ITelemetryTransport`：未来 Mac/iPhone/Android；
- `ISessionRepository`：未来云端；
- `IReferenceLapProvider`：未来高手圈；
- `ISetupShareService`：未来调教分享；
- `IRecommendationNarrator`：未来可选 AI 文本解释。

第一版只实现本地版本和空实现，不开发服务器。

---

## 20. Definition of Done

只有以下全部满足才叫“做完”：

- 安装后可启动；
- 能自动连接 LMU；
- 能点击开始并采集真实数据；
- 实时页面数据正确；
- 结束后保存/导出/丢弃都工作；
- 保存的会话能重开；
- 多圈、回放、双圈可用；
- 赛道和车辆位置正确；
- 弯道建议有证据；
- 调教建议有置信度和验证方法；
- 所有页面符合参考图；
- `dotnet test` 全绿；
- Release 构建在干净 Windows 电脑可运行；
- 没有旧 rFactor 2 DLL、注入或游戏内存写入。

---

## 21. 关键参考链接

详见 `reference/SOURCES.md`。实现前必须再次检查 LMU 最新 Header 和更新日志；本说明书中的结构是实现起点，不替代用户本地安装目录里的官方 Header。
