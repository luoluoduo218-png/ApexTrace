using System.Windows;
using System.Windows.Input;
using ApexTrace.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ApexTrace.Rendering;

public sealed class TrackCanvasControl : SKElement
{
    public static readonly DependencyProperty TrackPointsProperty = DependencyProperty.Register(
        nameof(TrackPoints), typeof(IReadOnlyList<TrackPoint>), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(Array.Empty<TrackPoint>(), FrameworkPropertyMetadataOptions.AffectsRender, (_, _) => { }));

    public static readonly DependencyProperty ReferencePointsProperty = DependencyProperty.Register(
        nameof(ReferencePoints), typeof(IReadOnlyList<TrackPoint>), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(Array.Empty<TrackPoint>(), FrameworkPropertyMetadataOptions.AffectsRender, (_, _) => { }));

    public static readonly DependencyProperty LapTracesProperty = DependencyProperty.Register(
        nameof(LapTraces), typeof(IReadOnlyList<LapTrace>), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(Array.Empty<LapTrace>(), FrameworkPropertyMetadataOptions.AffectsRender, (_, _) => { }));

    public static readonly DependencyProperty SelectedLapNumberProperty = DependencyProperty.Register(
        nameof(SelectedLapNumber), typeof(int), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentProgressProperty = DependencyProperty.Register(
        nameof(CurrentProgress), typeof(double), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events), typeof(IReadOnlyList<DrivingEvent>), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(Array.Empty<DrivingEvent>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReferenceColorProperty = DependencyProperty.Register(
        nameof(ReferenceColor), typeof(string), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata("#FF9D2E", FrameworkPropertyMetadataOptions.AffectsRender));

    private double _zoom = 1;
    private Vector _pan;
    private Point? _dragStart;
    private Vector _panAtDragStart;

    public TrackCanvasControl()
    {
        Focusable = true;
        PaintSurface += OnPaintSurface;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
        Cursor = Cursors.Hand;
    }

    public IReadOnlyList<TrackPoint> TrackPoints
    {
        get => (IReadOnlyList<TrackPoint>)GetValue(TrackPointsProperty);
        set => SetValue(TrackPointsProperty, value);
    }

    public IReadOnlyList<TrackPoint> ReferencePoints
    {
        get => (IReadOnlyList<TrackPoint>)GetValue(ReferencePointsProperty);
        set => SetValue(ReferencePointsProperty, value);
    }

    public IReadOnlyList<LapTrace> LapTraces
    {
        get => (IReadOnlyList<LapTrace>)GetValue(LapTracesProperty);
        set => SetValue(LapTracesProperty, value);
    }

    public int SelectedLapNumber
    {
        get => (int)GetValue(SelectedLapNumberProperty);
        set => SetValue(SelectedLapNumberProperty, value);
    }

    public double CurrentProgress
    {
        get => (double)GetValue(CurrentProgressProperty);
        set => SetValue(CurrentProgressProperty, value);
    }

    public IReadOnlyList<DrivingEvent> Events
    {
        get => (IReadOnlyList<DrivingEvent>)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public string ReferenceColor
    {
        get => (string)GetValue(ReferenceColorProperty);
        set => SetValue(ReferenceColorProperty, value);
    }

    public void FitToView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    public void ZoomBy(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.4, 8);
        InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse("#07111D"));
        DrawGrid(canvas, e.Info.Width, e.Info.Height);
        // Bindings may supply live collections. Hold one immutable render frame so a
        // producer cannot change Count between the bounds check and indexed access.
        var trackPoints = TrackPoints.ToArray();
        var referencePoints = ReferencePoints.ToArray();
        var events = Events.ToArray();
        var lapTraces = LapTraces
            .Where(trace => trace.Points.Count > 1)
            .Select(trace => trace with { Points = trace.Points.ToArray() })
            .ToArray();
        var transformPoints = trackPoints
            .Concat(referencePoints)
            .Concat(lapTraces.SelectMany(trace => trace.Points))
            .ToArray();
        if (transformPoints.Length < 2)
        {
            DrawEmptyMessage(canvas, e.Info.Width, e.Info.Height);
            return;
        }

        var transform = CalculateTransform(transformPoints, e.Info.Width, e.Info.Height);
        if (lapTraces.Length == 0)
        {
            if (trackPoints.Length > 1)
            {
                DrawPath(canvas, trackPoints, transform, SKColor.Parse("#183B66"), 12, false);
                DrawPath(canvas, trackPoints, transform, SKColor.Parse("#2B8CFF"), 5, false);
            }
            if (referencePoints.Length > 1)
            {
                DrawPath(canvas, referencePoints, transform, ParseColor(ReferenceColor, "#FF9D2E"), 3, true);
            }
            if (trackPoints.Length > 0)
            {
                if (events.Length > 0) DrawEventMarkers(canvas, trackPoints, events, transform);
                DrawStartFinish(canvas, trackPoints[0], transform);
                DrawCurrentPosition(canvas, trackPoints, transform);
            }
            else if (referencePoints.Length > 0)
            {
                DrawStartFinish(canvas, referencePoints[0], transform);
            }
            return;
        }

        if (trackPoints.Length > 1)
        {
            DrawPath(canvas, trackPoints, transform, SKColor.Parse("#132A43"), 11, false);
        }
        foreach (var trace in lapTraces.Where(trace => trace.LapNumber != SelectedLapNumber && !trace.IsBest)
                     .OrderBy(trace => trace.IsValid))
        {
            var color = trace.IsValid ? SKColor.Parse("#2B8CFF").WithAlpha(105) : SKColor.Parse("#7B8796").WithAlpha(135);
            DrawPath(canvas, trace.Points, transform, color, trace.IsValid ? 2.2f : 2.5f, !trace.IsValid);
        }
        foreach (var trace in lapTraces.Where(trace => trace.IsBest && trace.LapNumber != SelectedLapNumber))
        {
            DrawPath(canvas, trace.Points, transform, SKColor.Parse("#32B7FF").WithAlpha(230), 3.8f, false);
        }
        var selected = lapTraces.FirstOrDefault(trace => trace.LapNumber == SelectedLapNumber)
            ?? lapTraces.FirstOrDefault(trace => trace.IsBest)
            ?? lapTraces[^1];
        DrawPath(canvas, selected.Points, transform, SKColor.Parse("#0D4F98").WithAlpha(190), 9, false);
        DrawPath(canvas, selected.Points, transform, SKColor.Parse("#4E9DFF"), 4.5f, false);
        if (referencePoints.Length > 1)
        {
            DrawPath(canvas, referencePoints, transform, ParseColor(ReferenceColor, "#FF9D2E"), 3, true);
        }
        DrawStartFinish(canvas, selected.Points[0], transform);
        DrawCurrentPosition(canvas, selected.Points, transform);
    }

    private void DrawCurrentPosition(SKCanvas canvas, IReadOnlyList<TrackPoint> points, TransformInfo transform)
    {
        if (points.Count == 0) return;
        var index = Math.Clamp((int)Math.Round(CurrentProgress * (points.Count - 1)), 0, points.Count - 1);
        var current = Transform(points[index], transform);
        using var halo = new SKPaint { Color = SKColors.White.WithAlpha(65), IsAntialias = true };
        using var car = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(current, 12, halo);
        canvas.DrawCircle(current, 5, car);
    }

    private static void DrawGrid(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint { Color = SKColor.Parse("#102235"), StrokeWidth = 1 };
        for (var x = 0; x < width; x += 64) canvas.DrawLine(x, 0, x, height, paint);
        for (var y = 0; y < height; y += 64) canvas.DrawLine(0, y, width, y, paint);
    }

    private static void DrawEmptyMessage(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint { Color = SKColor.Parse("#233243"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawCircle(width / 2f, height / 2f, 28, paint);
        canvas.DrawLine(width / 2f - 12, height / 2f, width / 2f + 12, height / 2f, paint);
    }

    private static void DrawPath(SKCanvas canvas, IReadOnlyList<TrackPoint> points, TransformInfo transform, SKColor color, float width, bool dashed)
    {
        using var path = new SKPath();
        var first = Transform(points[0], transform);
        path.MoveTo(first);
        for (var index = 1; index < points.Count; index++) path.LineTo(Transform(points[index], transform));
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = width,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            PathEffect = dashed ? SKPathEffect.CreateDash([12, 10], 0) : null
        };
        canvas.DrawPath(path, paint);
    }

    private static void DrawStartFinish(SKCanvas canvas, TrackPoint point, TransformInfo transform)
    {
        var p = Transform(point, transform);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var dark = new SKPaint { Color = SKColor.Parse("#07111D"), IsAntialias = true };
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 2; column++)
        {
            canvas.DrawRect(p.X - 8 + column * 8, p.Y - 16 + row * 8, 8, 8, (row + column) % 2 == 0 ? white : dark);
        }
    }

    private static void DrawEventMarkers(SKCanvas canvas, IReadOnlyList<TrackPoint> points, IReadOnlyList<DrivingEvent> events, TransformInfo transform)
    {
        var visibleEvents = new List<DrivingEvent>();
        foreach (var item in events.Where(item => item.Type is DrivingEventType.BrakeStarted or DrivingEventType.TurnIn
                     or DrivingEventType.Apex or DrivingEventType.ThrottleStarted))
        {
            var sameCategoryNearby = visibleEvents.Any(existing => existing.Type == item.Type
                && Math.Abs(existing.LapDistanceMeters - item.LapDistanceMeters) < 120);
            if (!sameCategoryNearby) visibleEvents.Add(item);
            if (visibleEvents.Count == 18) break;
        }
        for (var index = 0; index < visibleEvents.Count; index++)
        {
            var item = visibleEvents[index];
            var nearest = points.MinBy(point => Math.Abs(point.DistanceMeters - item.LapDistanceMeters));
            if (nearest is null) continue;
            var position = Transform(nearest, transform);
            var color = item.Type switch
            {
                DrivingEventType.BrakeStarted or DrivingEventType.PeakBrake => SKColor.Parse("#F04444"),
                DrivingEventType.Apex or DrivingEventType.TurnIn => SKColor.Parse("#F5B942"),
                _ => SKColor.Parse("#20C76F")
            };
            using var halo = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var marker = new SKPaint { Color = color, IsAntialias = true };
            using var label = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 12 };
            canvas.DrawCircle(position, 6, halo);
            canvas.DrawCircle(position, 4, marker);
            canvas.DrawText($"T{index + 1}", position.X + 7, position.Y - 7, label);
        }
    }

    private static SKColor ParseColor(string? value, string fallback)
    {
        try { return SKColor.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value); }
        catch { return SKColor.Parse(fallback); }
    }

    private TransformInfo CalculateTransform(IReadOnlyList<TrackPoint> points, int width, int height)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var padding = Math.Clamp(Math.Min(width, height) * 0.12, 8, 40);
        var scale = Math.Min((width - padding * 2) / contentWidth, (height - padding * 2) / contentHeight) * _zoom;
        return new TransformInfo(minX, minY, scale,
            (width - contentWidth * scale) / 2 + _pan.X,
            (height - contentHeight * scale) / 2 + _pan.Y);
    }

    private static SKPoint Transform(TrackPoint point, TransformInfo transform) =>
        new((float)((point.X - transform.MinX) * transform.Scale + transform.OffsetX),
            (float)((point.Y - transform.MinY) * transform.Scale + transform.OffsetY));

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.12 : 0.89), 0.4, 8);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(this);
        _panAtDragStart = _pan;
        CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        _pan = _panAtDragStart + (current - _dragStart.Value);
        InvalidateVisual();
    }

    private sealed record TransformInfo(double MinX, double MinY, double Scale, double OffsetX, double OffsetY);
}
