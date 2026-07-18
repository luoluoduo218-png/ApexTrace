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

    public static readonly DependencyProperty CurrentProgressProperty = DependencyProperty.Register(
        nameof(CurrentProgress), typeof(double), typeof(TrackCanvasControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public double CurrentProgress
    {
        get => (double)GetValue(CurrentProgressProperty);
        set => SetValue(CurrentProgressProperty, value);
    }

    public void FitToView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse("#07111D"));
        DrawGrid(canvas, e.Info.Width, e.Info.Height);
        if (TrackPoints.Count < 2)
        {
            DrawEmptyMessage(canvas, e.Info.Width, e.Info.Height);
            return;
        }

        var transform = CalculateTransform(TrackPoints, e.Info.Width, e.Info.Height);
        if (ReferencePoints.Count > 1)
        {
            DrawPath(canvas, ReferencePoints, transform, SKColor.Parse("#FF9D2E"), 3, true);
        }

        DrawPath(canvas, TrackPoints, transform, SKColor.Parse("#183B66"), 12, false);
        DrawPath(canvas, TrackPoints, transform, SKColor.Parse("#2B8CFF"), 5, false);
        DrawStartFinish(canvas, TrackPoints[0], transform);

        var index = Math.Clamp((int)Math.Round(CurrentProgress * (TrackPoints.Count - 1)), 0, TrackPoints.Count - 1);
        var current = Transform(TrackPoints[index], transform);
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

    private TransformInfo CalculateTransform(IReadOnlyList<TrackPoint> points, int width, int height)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var scale = Math.Min((width - 80) / contentWidth, (height - 80) / contentHeight) * _zoom;
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
