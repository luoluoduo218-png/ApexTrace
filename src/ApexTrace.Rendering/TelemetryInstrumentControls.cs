using System.Windows;
using System.Windows.Input;
using ApexTrace.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ApexTrace.Rendering;

public sealed class TelemetryGaugeControl : SKElement
{
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), typeof(double), 100d);
    public static readonly DependencyProperty DisplayTextProperty = Register(nameof(DisplayText), typeof(string), "--");
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), typeof(string), string.Empty);
    public static readonly DependencyProperty AccentProperty = Register(nameof(Accent), typeof(string), "#2B8CFF");
    public static readonly DependencyProperty ShowRedZoneProperty = Register(nameof(ShowRedZone), typeof(bool), false);

    public TelemetryGaugeControl() => PaintSurface += OnPaintSurface;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string DisplayText { get => (string)GetValue(DisplayTextProperty); set => SetValue(DisplayTextProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Accent { get => (string)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool ShowRedZone { get => (bool)GetValue(ShowRedZoneProperty); set => SetValue(ShowRedZoneProperty, value); }

    private static DependencyProperty Register(string name, Type type, object value) => DependencyProperty.Register(
        name, type, typeof(TelemetryGaugeControl), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        var stroke = Math.Max(5, Math.Min(width, height) * 0.07f);
        var rect = new SKRect(stroke + 4, stroke + 5, width - stroke - 4, height * 1.55f);
        const float start = 200;
        const float sweep = 140;
        var ratio = (float)Math.Clamp(Maximum <= 0 ? 0 : Value / Maximum, 0, 1);

        using var background = new SKPaint { Color = SKColor.Parse("#263545"), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var accent = new SKPaint { Color = ParseColor(Accent, "#2B8CFF"), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        canvas.DrawArc(rect, start, sweep, false, background);
        if (ratio > 0) canvas.DrawArc(rect, start, sweep * ratio, false, accent);
        if (ShowRedZone)
        {
            using var red = new SKPaint { Color = SKColor.Parse("#F04444"), Style = SKPaintStyle.Stroke, StrokeWidth = stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            canvas.DrawArc(rect, start + sweep * 0.86f, sweep * 0.14f, false, red);
        }

        using var ticks = new SKPaint { Color = SKColor.Parse("#9AA8B7"), StrokeWidth = 1.2f, IsAntialias = true };
        var center = new SKPoint(rect.MidX, rect.MidY);
        for (var index = 0; index <= 10; index++)
        {
            var angle = (start + sweep * index / 10f) * MathF.PI / 180;
            var outer = new SKPoint(center.X + MathF.Cos(angle) * rect.Width / 2, center.Y + MathF.Sin(angle) * rect.Height / 2);
            var inner = new SKPoint(center.X + MathF.Cos(angle) * (rect.Width / 2 - 8), center.Y + MathF.Sin(angle) * (rect.Height / 2 - 8));
            canvas.DrawLine(inner, outer, ticks);
        }

        DrawCenteredText(canvas, DisplayText, width / 2f, height * 0.62f, Math.Min(width * 0.22f, height * 0.34f), SKColors.White, true);
        DrawCenteredText(canvas, Unit, width / 2f, height * 0.84f, Math.Min(width * 0.075f, 14), SKColor.Parse("#9AA8B7"), false);
    }

    internal static void DrawCenteredText(SKCanvas canvas, string? text, float x, float baseline, float size, SKColor color, bool bold)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, TextSize = Math.Max(8, size), Typeface = bold ? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) : SKTypeface.FromFamilyName("Segoe UI") };
        var value = string.IsNullOrWhiteSpace(text) ? "--" : text;
        canvas.DrawText(value, x - paint.MeasureText(value) / 2, baseline, paint);
    }

    private static SKColor ParseColor(string? value, string fallback)
    {
        try { return SKColor.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value); }
        catch { return SKColor.Parse(fallback); }
    }
}

public sealed class GForceIndicatorControl : SKElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(GForceIndicatorControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GForceIndicatorControl), new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HorizontalProperty = DependencyProperty.Register(nameof(Horizontal), typeof(bool), typeof(GForceIndicatorControl), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public GForceIndicatorControl() => PaintSurface += OnPaintSurface;
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool Horizontal { get => (bool)GetValue(HorizontalProperty); set => SetValue(HorizontalProperty, value); }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var center = new SKPoint(e.Info.Width / 2f, e.Info.Height / 2f);
        var radius = Math.Max(4, Math.Min(e.Info.Width, e.Info.Height) / 2f - 4);
        using var ring = new SKPaint { Color = SKColor.Parse("#334252"), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        for (var index = 1; index <= 3; index++) canvas.DrawCircle(center, radius * index / 3, ring);
        canvas.DrawLine(center.X - radius, center.Y, center.X + radius, center.Y, ring);
        canvas.DrawLine(center.X, center.Y - radius, center.X, center.Y + radius, ring);
        var offset = (float)(Math.Clamp(Value / Math.Max(0.01, Maximum), -1, 1) * radius * 0.8);
        var dot = Horizontal ? new SKPoint(center.X + offset, center.Y) : new SKPoint(center.X, center.Y - offset);
        using var halo = new SKPaint { Color = SKColor.Parse("#284F7D"), IsAntialias = true };
        using var point = new SKPaint { Color = SKColor.Parse("#1677FF"), IsAntialias = true };
        canvas.DrawCircle(dot, 7, halo);
        canvas.DrawCircle(dot, 3.5f, point);
    }
}

public sealed class SteeringIndicatorControl : SKElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SteeringIndicatorControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public SteeringIndicatorControl() => PaintSurface += OnPaintSurface;
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        var center = new SKPoint(width / 2f, height / 2f);
        var radius = MathF.Max(8, MathF.Min(width, height) * 0.42f);
        var stroke = MathF.Max(2, radius * 0.105f);
        using var rim = new SKPaint
        {
            Color = SKColor.Parse("#DDE6F0"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var spoke = new SKPaint
        {
            Color = SKColor.Parse("#8EA0B3"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2, stroke * 0.62f),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var hub = new SKPaint { Color = SKColor.Parse("#24384D"), IsAntialias = true };
        using var centerMarker = new SKPaint { Color = SKColor.Parse("#2B8CFF"), StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round, IsAntialias = true };

        canvas.Save();
        canvas.RotateDegrees((float)Value, center.X, center.Y);
        canvas.DrawCircle(center, radius, rim);
        for (var index = 0; index < 3; index++)
        {
            var angle = (-90 + index * 120) * MathF.PI / 180;
            var inner = radius * 0.2f;
            var outer = radius * 0.84f;
            canvas.DrawLine(
                center.X + MathF.Cos(angle) * inner,
                center.Y + MathF.Sin(angle) * inner,
                center.X + MathF.Cos(angle) * outer,
                center.Y + MathF.Sin(angle) * outer,
                spoke);
        }
        canvas.DrawCircle(center, radius * 0.23f, hub);
        canvas.DrawLine(center.X, center.Y - radius * 0.98f, center.X, center.Y - radius * 0.73f, centerMarker);
        canvas.Restore();
    }
}

public sealed class ReplayTimelineControl : SKElement
{
    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(nameof(Events), typeof(IReadOnlyList<DrivingEvent>), typeof(ReplayTimelineControl), new FrameworkPropertyMetadata(Array.Empty<DrivingEvent>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartSecondsProperty = DependencyProperty.Register(nameof(StartSeconds), typeof(double), typeof(ReplayTimelineControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(nameof(DurationSeconds), typeof(double), typeof(ReplayTimelineControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ReplayTimelineControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public ReplayTimelineControl()
    {
        PaintSurface += OnPaintSurface;
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); SetProgress(e.GetPosition(this).X); };
        MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetProgress(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        Cursor = Cursors.SizeWE;
    }

    public IReadOnlyList<DrivingEvent> Events { get => GetValue(EventsProperty) as IReadOnlyList<DrivingEvent> ?? Array.Empty<DrivingEvent>(); set => SetValue(EventsProperty, value); }
    public double StartSeconds { get => (double)GetValue(StartSecondsProperty); set => SetValue(StartSecondsProperty, value); }
    public double DurationSeconds { get => (double)GetValue(DurationSecondsProperty); set => SetValue(DurationSecondsProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private void SetProgress(double x) => Progress = Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1);

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        var baseline = height - 18f;
        using var line = new SKPaint { Color = SKColor.Parse("#25415F"), StrokeWidth = 2, IsAntialias = true };
        using var waveform = new SKPaint { Color = SKColor.Parse("#1767C5"), StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(0, baseline, width, baseline, line);
        for (var x = 0; x < width; x += 4)
        {
            var heightOffset = 2 + (x % 20) / 5f;
            canvas.DrawLine(x, baseline - heightOffset, x, baseline + heightOffset, waveform);
        }
        for (var index = 0; index <= 10; index++)
        {
            var x = width * index / 10f;
            canvas.DrawLine(x, baseline - 8, x, baseline + 8, line);
        }

        var duration = Math.Max(0.001, DurationSeconds);
        var endSeconds = StartSeconds + duration;
        var lastLabelRight = new[] { float.NegativeInfinity, float.NegativeInfinity };
        foreach (var item in Events
                     .Where(item => item.SessionElapsedSeconds >= StartSeconds - 0.001
                         && item.SessionElapsedSeconds <= endSeconds + 0.001)
                     .OrderBy(item => item.SessionElapsedSeconds))
        {
            var ratio = Math.Clamp((item.SessionElapsedSeconds - StartSeconds) / duration, 0, 1);
            var x = (float)(ratio * width);
            var (color, label) = TimelineEventStyle(item);
            using var marker = new SKPaint { Color = color, IsAntialias = true };
            using var markerLine = new SKPaint { Color = color.WithAlpha(150), StrokeWidth = 1.2f, IsAntialias = true };
            using var text = new SKPaint { Color = color, TextSize = 10, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI") };
            using var path = new SKPath();
            path.MoveTo(x, baseline - 3);
            path.LineTo(x - 5, baseline - 11);
            path.LineTo(x + 5, baseline - 11);
            path.Close();
            canvas.DrawPath(path, marker);

            var labelWidth = text.MeasureText(label);
            var labelLeft = Math.Clamp(x - labelWidth / 2, 1, Math.Max(1, width - labelWidth - 1));
            var lane = labelLeft > lastLabelRight[0] + 4 ? 0 : labelLeft > lastLabelRight[1] + 4 ? 1 : -1;
            if (lane < 0)
            {
                canvas.DrawLine(x, baseline - 12, x, baseline - 4, markerLine);
                continue;
            }

            var labelBaseline = baseline - 17 - lane * 12;
            canvas.DrawLine(x, labelBaseline + 3, x, baseline - 4, markerLine);
            canvas.DrawText(label, labelLeft, labelBaseline, text);
            lastLabelRight[lane] = labelLeft + labelWidth;
        }

        var progressX = (float)(Math.Clamp(Progress, 0, 1) * width);
        using var cursor = new SKPaint { Color = SKColors.White, StrokeWidth = 2, IsAntialias = true };
        using var knob = new SKPaint { Color = SKColor.Parse("#F4F7FB"), IsAntialias = true };
        canvas.DrawLine(progressX, 2, progressX, baseline + 9, cursor);
        canvas.DrawCircle(progressX, baseline + 9, 8, knob);
    }

    private static (SKColor Color, string Label) TimelineEventStyle(DrivingEvent item) => item.Type switch
    {
        DrivingEventType.LapStarted => (SKColor.Parse("#52B6FF"), "开始"),
        DrivingEventType.LapCompleted => (SKColor.Parse("#52B6FF"), "完成"),
        DrivingEventType.BrakeStarted => (SKColor.Parse("#F04444"), "刹车"),
        DrivingEventType.PeakBrake => (SKColor.Parse("#FF6B6B"), "峰值"),
        DrivingEventType.BrakeReleased => (SKColor.Parse("#FF8A80"), "松刹"),
        DrivingEventType.TurnIn => (SKColor.Parse("#F5B942"), "入弯"),
        DrivingEventType.Apex => (SKColor.Parse("#FFD166"), "弯心"),
        DrivingEventType.ThrottleStarted => (SKColor.Parse("#20C76F"), "补油"),
        DrivingEventType.FullThrottle => (SKColor.Parse("#55DE91"), "全油"),
        DrivingEventType.GearShift => (SKColor.Parse("#2B8CFF"), $"G{item.Value:F0}"),
        DrivingEventType.AbsActivation => (SKColor.Parse("#B66DFF"), "ABS"),
        DrivingEventType.TcActivation => (SKColor.Parse("#42D6FF"), "TC"),
        DrivingEventType.OffTrack => (SKColor.Parse("#FF5C8A"), "出界"),
        DrivingEventType.LapInvalidated => (SKColor.Parse("#FF5C8A"), "无效"),
        DrivingEventType.PitEntry => (SKColor.Parse("#A7B4C2"), "进站"),
        DrivingEventType.PitExit => (SKColor.Parse("#A7B4C2"), "出站"),
        DrivingEventType.Impact => (SKColor.Parse("#FF3355"), "碰撞"),
        _ => (SKColor.Parse("#2B8CFF"), item.Type.ToString())
    };
}

public sealed class PedalTraceControl : SKElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<TelemetrySample>), typeof(PedalTraceControl),
        new FrameworkPropertyMetadata(Array.Empty<TelemetrySample>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CurrentTimeSecondsProperty = DependencyProperty.Register(
        nameof(CurrentTimeSeconds), typeof(double), typeof(PedalTraceControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThrottleValueProperty = DependencyProperty.Register(
        nameof(ThrottleValue), typeof(double), typeof(PedalTraceControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrakeValueProperty = DependencyProperty.Register(
        nameof(BrakeValue), typeof(double), typeof(PedalTraceControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public PedalTraceControl() => PaintSurface += OnPaintSurface;

    public IReadOnlyList<TelemetrySample> Samples
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double CurrentTimeSeconds
    {
        get => (double)GetValue(CurrentTimeSecondsProperty);
        set => SetValue(CurrentTimeSecondsProperty, value);
    }

    public double ThrottleValue
    {
        get => (double)GetValue(ThrottleValueProperty);
        set => SetValue(ThrottleValueProperty, value);
    }

    public double BrakeValue
    {
        get => (double)GetValue(BrakeValueProperty);
        set => SetValue(BrakeValueProperty, value);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        if (width < 120 || height < 72) return;

        var plot = new SKRect(47, 31, width - 12, height - 25);
        if (plot.Width <= 40 || plot.Height <= 24) return;

        var throttleColor = SKColor.Parse("#20C76F");
        var brakeColor = SKColor.Parse("#F04444");
        var secondaryColor = SKColor.Parse("#9AA8B7");
        var gridColor = SKColor.Parse("#213246");
        var axisColor = SKColor.Parse("#62748A");

        using var titlePaint = CreateTextPaint(SKColors.White, 12, true);
        canvas.DrawText("踏板开合度", 2, 15, titlePaint);
        var throttleLegend = $"油门 {Math.Clamp(ThrottleValue, 0, 1):P0}";
        var brakeLegend = $"刹车 {Math.Clamp(BrakeValue, 0, 1):P0}";
        using var legendText = CreateTextPaint(secondaryColor, 9, false);
        var brakeLegendWidth = legendText.MeasureText(brakeLegend) + 22;
        var throttleLegendWidth = legendText.MeasureText(throttleLegend) + 22;
        DrawLegend(canvas, width - brakeLegendWidth, 12, brakeColor, brakeLegend, secondaryColor);
        DrawLegend(canvas, width - brakeLegendWidth - throttleLegendWidth - 8, 12, throttleColor, throttleLegend, secondaryColor);

        using var plotBackground = new SKPaint { Color = SKColor.Parse("#07121D"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(plot, plotBackground);
        using var gridPaint = new SKPaint { Color = gridColor, StrokeWidth = 1, IsAntialias = true };
        using var axisPaint = new SKPaint { Color = axisColor, StrokeWidth = 1.2f, IsAntialias = true };
        using var labelPaint = CreateTextPaint(secondaryColor, 9, false);
        for (var index = 0; index <= 4; index++)
        {
            var ratio = index / 4f;
            var y = plot.Bottom - ratio * plot.Height;
            canvas.DrawLine(plot.Left, y, plot.Right, y, gridPaint);
            var label = $"{index * 25}%";
            canvas.DrawText(label, plot.Left - labelPaint.MeasureText(label) - 5, y + 3, labelPaint);
        }

        const double historySeconds = 8;
        var nowX = plot.Right;
        for (var index = 0; index <= 4; index++)
        {
            var ratio = index / 4f;
            var x = plot.Left + ratio * plot.Width;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom, gridPaint);
            var secondsAgo = historySeconds * (1 - ratio);
            var label = index == 4 ? "现在" : $"-{secondsAgo:0}s";
            var labelX = Math.Clamp(x - labelPaint.MeasureText(label) / 2, plot.Left, plot.Right - labelPaint.MeasureText(label));
            canvas.DrawText(label, labelX, plot.Bottom + 12, labelPaint);
        }

        canvas.DrawLine(plot.Left, plot.Top, plot.Left, plot.Bottom, axisPaint);
        canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom, axisPaint);
        using var nowPaint = new SKPaint { Color = SKColor.Parse("#2B8CFF"), StrokeWidth = 1.3f, IsAntialias = true };
        canvas.DrawLine(nowX, plot.Top, nowX, plot.Bottom, nowPaint);
        const string unitLabel = "历史向左滚动  ←  时间";
        canvas.DrawText(unitLabel, plot.Right - labelPaint.MeasureText(unitLabel), height - 2, labelPaint);

        var (visibleStart, visibleEnd) = FindVisibleSampleRange(CurrentTimeSeconds, historySeconds);
        canvas.Save();
        canvas.ClipRect(plot);
        DrawTrace(canvas, plot, CurrentTimeSeconds, historySeconds, visibleStart, visibleEnd, sample => sample.Throttle, throttleColor);
        DrawTrace(canvas, plot, CurrentTimeSeconds, historySeconds, visibleStart, visibleEnd, sample => sample.Brake, brakeColor);
        DrawCurrentPosition(canvas, plot, throttleColor, brakeColor);
        canvas.Restore();

        if (visibleEnd - visibleStart < 2)
        {
            using var emptyPaint = CreateTextPaint(secondaryColor, 10, false);
            const string emptyText = "播放后，踏板变化将从右端向左展开";
            canvas.DrawText(emptyText, plot.Left + 10, plot.MidY + 4, emptyPaint);
        }
    }

    private void DrawTrace(
        SKCanvas canvas,
        SKRect plot,
        double currentTimeSeconds,
        double historySeconds,
        int visibleStart,
        int visibleEnd,
        Func<TelemetrySample, double> valueSelector,
        SKColor color)
    {
        if (visibleEnd <= visibleStart || !double.IsFinite(currentTimeSeconds)) return;

        using var path = new SKPath();
        var started = false;
        var maximumPoints = Math.Max(100, (int)plot.Width * 2);
        var stride = Math.Max(1, (visibleEnd - visibleStart) / maximumPoints);
        for (var index = visibleStart; index < visibleEnd; index += stride)
        {
            AddPoint(Samples[index]);
        }
        if ((visibleEnd - 1 - visibleStart) % stride != 0) AddPoint(Samples[visibleEnd - 1]);

        using var glow = new SKPaint
        {
            Color = color.WithAlpha(45),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        using var line = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.1f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        if (started)
        {
            canvas.DrawPath(path, glow);
            canvas.DrawPath(path, line);
        }
        return;

        void AddPoint(TelemetrySample sample)
        {
            var value = valueSelector(sample);
            if (!double.IsFinite(value)) return;
            var secondsAgo = currentTimeSeconds - sample.SessionElapsedSeconds;
            var x = plot.Right - (float)(Math.Clamp(secondsAgo / historySeconds, 0, 1) * plot.Width);
            var y = plot.Bottom - (float)(Math.Clamp(value, 0, 1) * plot.Height);
            if (started) path.LineTo(x, y);
            else
            {
                path.MoveTo(x, y);
                started = true;
            }
        }
    }

    private (int Start, int End) FindVisibleSampleRange(double currentTimeSeconds, double historySeconds)
    {
        if (!double.IsFinite(currentTimeSeconds) || Samples.Count == 0) return (0, 0);
        var windowStart = currentTimeSeconds - historySeconds;
        var start = 0;
        while (start < Samples.Count && Samples[start].SessionElapsedSeconds < windowStart) start++;
        var end = start;
        while (end < Samples.Count && Samples[end].SessionElapsedSeconds <= currentTimeSeconds + 0.001) end++;
        return (start, end);
    }

    private void DrawCurrentPosition(SKCanvas canvas, SKRect plot, SKColor throttleColor, SKColor brakeColor)
    {
        var x = plot.Right;

        using var throttlePoint = new SKPaint { Color = throttleColor, IsAntialias = true };
        using var brakePoint = new SKPaint { Color = brakeColor, IsAntialias = true };
        canvas.DrawCircle(x, plot.Bottom - (float)(Math.Clamp(ThrottleValue, 0, 1) * plot.Height), 3.4f, throttlePoint);
        canvas.DrawCircle(x, plot.Bottom - (float)(Math.Clamp(BrakeValue, 0, 1) * plot.Height), 3.4f, brakePoint);
    }

    private static void DrawLegend(SKCanvas canvas, float x, float y, SKColor color, string label, SKColor textColor)
    {
        using var line = new SKPaint { Color = color, StrokeWidth = 2, IsAntialias = true };
        using var text = CreateTextPaint(textColor, 9, false);
        canvas.DrawLine(x, y - 3, x + 12, y - 3, line);
        canvas.DrawText(label, x + 16, y, text);
    }

    private static SKPaint CreateTextPaint(SKColor color, float size, bool bold) => new()
    {
        Color = color,
        TextSize = size,
        IsAntialias = true,
        Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI", bold ? SKFontStyle.Bold : SKFontStyle.Normal)
    };
}

public sealed class LapComparisonChartControl : SKElement
{
    public static readonly DependencyProperty CurrentSamplesProperty = DependencyProperty.Register(
        nameof(CurrentSamples), typeof(IReadOnlyList<TelemetrySample>), typeof(LapComparisonChartControl),
        new FrameworkPropertyMetadata(Array.Empty<TelemetrySample>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReferenceSamplesProperty = DependencyProperty.Register(
        nameof(ReferenceSamples), typeof(IReadOnlyList<TelemetrySample>), typeof(LapComparisonChartControl),
        new FrameworkPropertyMetadata(Array.Empty<TelemetrySample>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowPedalsProperty = DependencyProperty.Register(
        nameof(ShowPedals), typeof(bool), typeof(LapComparisonChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public LapComparisonChartControl() => PaintSurface += OnPaintSurface;

    public IReadOnlyList<TelemetrySample> CurrentSamples
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(CurrentSamplesProperty);
        set => SetValue(CurrentSamplesProperty, value);
    }

    public IReadOnlyList<TelemetrySample> ReferenceSamples
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(ReferenceSamplesProperty);
        set => SetValue(ReferenceSamplesProperty, value);
    }

    public bool ShowPedals
    {
        get => (bool)GetValue(ShowPedalsProperty);
        set => SetValue(ShowPedalsProperty, value);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        if (width < 100 || height < 60) return;

        var plot = new SKRect(38, 8, width - 10, height - 22);
        var distanceMaximum = CurrentSamples.Concat(ReferenceSamples)
            .Where(sample => sample.Quality.IsLapDistanceValid && double.IsFinite(sample.LapDistanceMeters) && sample.LapDistanceMeters >= 0)
            .Select(sample => sample.LapDistanceMeters).DefaultIfEmpty(100).Max();
        distanceMaximum = Math.Max(100, distanceMaximum);
        var valueMaximum = ShowPedals
            ? 1d
            : Math.Max(100, Math.Ceiling(CurrentSamples.Concat(ReferenceSamples)
                .Select(sample => sample.SpeedMetersPerSecond * 3.6).DefaultIfEmpty(100).Max() / 50) * 50);

        using var grid = new SKPaint { Color = SKColor.Parse("#223346"), StrokeWidth = 1, IsAntialias = true };
        using var axisText = new SKPaint { Color = SKColor.Parse("#9AA8B7"), TextSize = 9, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI") };
        for (var index = 0; index <= 2; index++)
        {
            var ratio = index / 2f;
            var y = plot.Bottom - ratio * plot.Height;
            canvas.DrawLine(plot.Left, y, plot.Right, y, grid);
            var label = ShowPedals ? $"{index * 50}%" : $"{valueMaximum * ratio:F0}";
            canvas.DrawText(label, plot.Left - axisText.MeasureText(label) - 5, y + 3, axisText);
        }
        for (var index = 0; index <= 6; index++)
        {
            var ratio = index / 6f;
            var x = plot.Left + ratio * plot.Width;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom, grid);
            var label = (distanceMaximum * ratio / 1000).ToString("F1");
            canvas.DrawText(label, Math.Clamp(x - axisText.MeasureText(label) / 2, plot.Left, plot.Right - axisText.MeasureText(label)), plot.Bottom + 12, axisText);
        }
        using var sector = new SKPaint { Color = SKColor.Parse("#647487"), StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash([4, 4], 0) };
        for (var index = 1; index <= 2; index++)
        {
            var x = plot.Left + plot.Width * index / 3;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom, sector);
            canvas.DrawText($"S{index + 1}", x + 4, plot.Top + 11, axisText);
        }
        canvas.DrawText("S1", plot.Left + 4, plot.Top + 11, axisText);
        canvas.DrawText("赛道距离 (km)", plot.Right - axisText.MeasureText("赛道距离 (km)"), height - 2, axisText);

        if (ShowPedals)
        {
            DrawChannel(canvas, CurrentSamples, plot, distanceMaximum, valueMaximum, sample => sample.Throttle, SKColor.Parse("#2B8CFF"), false);
            DrawChannel(canvas, CurrentSamples, plot, distanceMaximum, valueMaximum, sample => sample.Brake, SKColor.Parse("#2B8CFF"), true);
            DrawChannel(canvas, ReferenceSamples, plot, distanceMaximum, valueMaximum, sample => sample.Throttle, SKColor.Parse("#FF9D2E"), false);
            DrawChannel(canvas, ReferenceSamples, plot, distanceMaximum, valueMaximum, sample => sample.Brake, SKColor.Parse("#FF9D2E"), true);
        }
        else
        {
            DrawChannel(canvas, CurrentSamples, plot, distanceMaximum, valueMaximum, sample => sample.SpeedMetersPerSecond * 3.6, SKColor.Parse("#2B8CFF"), false);
            DrawChannel(canvas, ReferenceSamples, plot, distanceMaximum, valueMaximum, sample => sample.SpeedMetersPerSecond * 3.6, SKColor.Parse("#FF9D2E"), false);
        }
    }

    private static void DrawChannel(
        SKCanvas canvas,
        IReadOnlyList<TelemetrySample> samples,
        SKRect plot,
        double distanceMaximum,
        double valueMaximum,
        Func<TelemetrySample, double> selector,
        SKColor color,
        bool dashed)
    {
        if (samples.Count == 0) return;
        using var path = new SKPath();
        var started = false;
        var stride = Math.Max(1, samples.Count / Math.Max(100, (int)plot.Width * 2));
        for (var index = 0; index < samples.Count; index += stride) Add(samples[index]);
        if ((samples.Count - 1) % stride != 0) Add(samples[^1]);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = dashed ? 1.3f : 1.8f,
            IsAntialias = true,
            StrokeJoin = SKStrokeJoin.Round,
            PathEffect = dashed ? SKPathEffect.CreateDash([5, 3], 0) : null
        };
        if (started) canvas.DrawPath(path, paint);
        return;

        void Add(TelemetrySample sample)
        {
            if (!sample.Quality.IsLapDistanceValid || sample.LapDistanceMeters < 0) return;
            var x = plot.Left + (float)(Math.Clamp(sample.LapDistanceMeters / distanceMaximum, 0, 1) * plot.Width);
            var y = plot.Bottom - (float)(Math.Clamp(selector(sample) / valueMaximum, 0, 1) * plot.Height);
            if (started) path.LineTo(x, y);
            else
            {
                path.MoveTo(x, y);
                started = true;
            }
        }
    }
}
