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

        var tcSamples = session.Samples.Where(sample => sample.TcActive && sample.Throttle > 0.85).ToArray();
        if (tcSamples.Length < session.Samples.Count * 0.02)
        {
            return [];
        }

        var first = tcSamples[0];
        return
        [
            new Recommendation(
                1,
                Guid.NewGuid(),
                "Driving",
                "Session",
                "高油门阶段 TC 介入偏多",
                [
                    $"{tcSamples.Length} 个样本在油门 >85% 时记录到 TC 介入",
                    $"首次集中介入位于圈 {first.LapNumber}、{first.LapDistanceMeters:F1}m"
                ],
                tcSamples.Length / (double)session.Samples.Count * 100,
                null,
                "% samples",
                0.05,
                0.25,
                Math.Min(0.9, 0.55 + validCompleteLaps.Length * 0.05),
                ["保持相近燃油、轮胎和天气，再完成 3 个有效圈", "逐步平顺补油并比较 TC 介入距离与出弯速度"])
        ];
    }
}
