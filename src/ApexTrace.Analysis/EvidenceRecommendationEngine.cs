using ApexTrace.Core;

namespace ApexTrace.Analysis;

public sealed class EvidenceRecommendationEngine
{
    public IReadOnlyList<Recommendation> Analyze(TelemetrySession session)
    {
        var validCompleteLaps = session.Laps.Where(lap => lap.IsComplete && lap.IsValid).ToArray();
        if (validCompleteLaps.Length < 3)
        {
            return [];
        }

        var recommendations = new List<Recommendation>();
        var recentLaps = validCompleteLaps.TakeLast(Math.Min(5, validCompleteLaps.Length)).ToArray();
        var orderedTimes = recentLaps.Select(lap => lap.LapTimeSeconds).Order().ToArray();
        var predictedLapTime = orderedTimes[orderedTimes.Length / 2];
        var spread = orderedTimes[^1] - orderedTimes[0];
        var lastLapTime = recentLaps[^1].LapTimeSeconds;
        var confidence = Math.Clamp(0.9 - spread / Math.Max(1, predictedLapTime), 0.55, 0.9);
        recommendations.Add(new Recommendation(
            1,
            Guid.NewGuid(),
            "Prediction",
            "Next valid lap",
            $"下一有效圈基准预测：{predictedLapTime:F3} s",
            [
                $"最近 {recentLaps.Length} 个完整有效圈：{string.Join(", ", recentLaps.Select(lap => $"{lap.LapTimeSeconds:F3}s"))}",
                $"圈时范围 {orderedTimes[0]:F3}–{orderedTimes[^1]:F3}s，跨度 {spread:F3}s"
            ],
            lastLapTime,
            predictedLapTime,
            "s",
            Math.Max(0, lastLapTime - predictedLapTime),
            Math.Max(0, lastLapTime - predictedLapTime),
            confidence,
            ["在相近燃油、轮胎和天气条件下完成下一有效圈", "将实测圈时与预测基准比较；条件明显变化时重新建立基准"]));

        var analyzedSamples = session.Samples.Where(sample => validCompleteLaps.Any(lap =>
            sample.LapNumber == lap.LapNumber
            && sample.SessionElapsedSeconds >= lap.StartedAtSeconds
            && sample.SessionElapsedSeconds <= lap.EndedAtSeconds)).ToArray();
        var brakingSamples = analyzedSamples.Where(sample => sample.Brake >= 0.15).ToArray();
        var absSamples = brakingSamples.Where(sample => sample.AbsActive).ToArray();
        var controls = analyzedSamples.LastOrDefault()?.Controls;
        if (brakingSamples.Length > 0 && absSamples.Length >= brakingSamples.Length * 0.03)
        {
            var rate = absSamples.Length / (double)brakingSamples.Length;
            recommendations.Add(new Recommendation(
                1,
                Guid.NewGuid(),
                "Setup",
                "ABS / braking",
                $"ABS {controls?.Abs ?? 0}：建议做相邻一级 A/B 测试",
                [
                    $"{validCompleteLaps.Length} 个有效圈的重刹样本中，ABS 介入占 {rate:P1}",
                    $"介入覆盖 {absSamples.Select(sample => sample.LapNumber).Distinct().Count()} 个圈，并非单次偶发"
                ],
                controls?.Abs,
                null,
                "level",
                0,
                0.2,
                Math.Clamp(0.55 + validCompleteLaps.Length * 0.05, 0.6, 0.85),
                ["先保持当前值完成 2 圈作为基线", "仅改 ABS 相邻一级再完成 2 圈", "比较制动距离、ABS 介入率和弯心最低速度；若变差立即恢复"]));
        }

        var highThrottleSamples = analyzedSamples.Where(sample => sample.Throttle > 0.75).ToArray();
        var tcSamples = highThrottleSamples.Where(sample => sample.TcActive).ToArray();
        if (highThrottleSamples.Length > 0 && tcSamples.Length >= highThrottleSamples.Length * 0.01)
        {
            var first = tcSamples[0];
            recommendations.Add(new Recommendation(
                1,
                Guid.NewGuid(),
                "Setup",
                "TC / corner exit",
                $"TC Slip {controls?.TractionControlSlip ?? 0} / Cut {controls?.TractionControlCut ?? 0}：建议单变量 A/B 测试",
                [
                    $"{tcSamples.Length} 个样本在油门 >75% 时记录到 TC 介入（占高油门样本 {tcSamples.Length / (double)highThrottleSamples.Length:P1}）",
                    $"首次集中介入位于圈 {first.LapNumber}、{first.LapDistanceMeters:F1}m"
                ],
                controls?.TractionControlSlip,
                null,
                "level",
                0,
                0.25,
                Math.Min(0.9, 0.55 + validCompleteLaps.Length * 0.05),
                ["保持燃油、轮胎和天气接近，只调整 TC Slip 或 TC Cut 其中一项", "各跑 2 个有效圈，比较介入距离、出弯油门和圈时", "不直接写回游戏；由车手确认后手动调整"]));
        }

        if (recommendations.All(recommendation => recommendation.Type != "Setup"))
        {
            recommendations.Add(new Recommendation(
                1, Guid.NewGuid(), "Setup", "Baseline", "电子辅助介入稳定，暂不建议改动调教",
                [$"已检查 {validCompleteLaps.Length} 个完整有效圈", "ABS/TC 介入率未达到重复性调教阈值"],
                null, null, string.Empty, 0, 0, 0.75,
                ["保留当前设定作为基线", "先用双圈对比的分段数据定位可重复的时间损失，再做单变量调教"]));
        }
        return recommendations;
    }
}
