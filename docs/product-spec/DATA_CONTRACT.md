# ApexTrace 数据契约摘要

## 统一单位

- 距离：米
- 速度：米/秒（UI 转 km/h）
- 时间：秒，显示层格式化
- 温度：摄氏度；LMU 轮胎部分字段如为 Kelvin，适配层转换
- 压力：kPa
- 角度：领域层用弧度，UI 可显示度
- 踏板：0.0-1.0

## 质量标记

每个样本都包含：

```text
IsConsistentSnapshot
IsPlayerResolved
IsLapDistanceValid
IsWorldPositionValid
SourceSequence
TelemetryEventCounter
ScoringEventCounter
```

## 关键模型

```text
TelemetrySample
SessionMetadata
LapRecord
TrackDefinition
TrackPolylinePoint
VehicleSnapshot
SetupSnapshot
DrivingEvent
CornerDefinition
CornerPerformance
TargetLinePoint
Recommendation
```

## 目标线点

```text
DistanceMeters
WorldX / WorldY / WorldZ
TargetSpeedMps
TargetThrottle
TargetBrake
Confidence
SourceLapIds[]
```

## Recommendation

```text
Id
Type
Scope (Session/Corner/Setup)
Title
Evidence[]
CurrentValue
SuggestedValue
Unit
EstimatedGainMinSeconds
EstimatedGainMaxSeconds
Confidence
ValidationSteps[]
```
