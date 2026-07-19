using System.Windows;
using ApexTrace.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ApexTrace.Rendering;

/// <summary>
/// Compact four-corner tire and brake overview used by both live telemetry and replay.
/// </summary>
public sealed class TireBrakeStatusControl : SKElement
{
    public static readonly DependencyProperty WheelsProperty = DependencyProperty.Register(
        nameof(Wheels), typeof(IReadOnlyList<WheelSample>), typeof(TireBrakeStatusControl),
        new FrameworkPropertyMetadata(Array.Empty<WheelSample>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrontCompoundProperty = DependencyProperty.Register(
        nameof(FrontCompound), typeof(string), typeof(TireBrakeStatusControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RearCompoundProperty = DependencyProperty.Register(
        nameof(RearCompound), typeof(string), typeof(TireBrakeStatusControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public TireBrakeStatusControl() => PaintSurface += OnPaintSurface;

    public IReadOnlyList<WheelSample> Wheels
    {
        get => GetValue(WheelsProperty) as IReadOnlyList<WheelSample> ?? Array.Empty<WheelSample>();
        set => SetValue(WheelsProperty, value);
    }

    public string FrontCompound
    {
        get => GetValue(FrontCompoundProperty) as string ?? string.Empty;
        set => SetValue(FrontCompoundProperty, value);
    }

    public string RearCompound
    {
        get => GetValue(RearCompoundProperty) as string ?? string.Empty;
        set => SetValue(RearCompoundProperty, value);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var width = e.Info.Width;
        var height = e.Info.Height;
        if (width < 80 || height < 56) return;

        var axleHeight = height / 2f;
        var ringRadius = Math.Clamp(Math.Min(width * 0.10f, axleHeight * 0.34f), 12f, 31f);
        var leftRingX = ringRadius + 3;
        var rightRingX = width - ringRadius - 3;
        var innerLeft = leftRingX + ringRadius + 5;
        var innerRight = rightRingX - ringRadius - 5;
        var innerWidth = Math.Max(28, innerRight - innerLeft);
        var valueTextSize = Math.Clamp(Math.Min(innerWidth * 0.087f, axleHeight * 0.16f), 7.5f, 12f);

        using var divider = new SKPaint
        {
            Color = SKColor.Parse("#24384D"),
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawLine(innerLeft, axleHeight, innerRight, axleHeight, divider);

        var frontCompound = string.IsNullOrWhiteSpace(FrontCompound) ? RearCompound : FrontCompound;
        var rearCompound = string.IsNullOrWhiteSpace(RearCompound) ? FrontCompound : RearCompound;
        DrawAxle(canvas, 0, axleHeight * 0.5f, frontCompound, leftRingX, rightRingX,
            innerLeft, innerRight, innerWidth, axleHeight, ringRadius, valueTextSize, divider);
        DrawAxle(canvas, 2, axleHeight * 1.5f, rearCompound, leftRingX, rightRingX,
            innerLeft, innerRight, innerWidth, axleHeight, ringRadius, valueTextSize, divider);
    }

    private void DrawAxle(
        SKCanvas canvas,
        int firstWheelIndex,
        float centerY,
        string compound,
        float leftRingX,
        float rightRingX,
        float innerLeft,
        float innerRight,
        float innerWidth,
        float axleHeight,
        float ringRadius,
        float valueTextSize,
        SKPaint divider)
    {
        var leftWheel = WheelAt(firstWheelIndex);
        var rightWheel = WheelAt(firstWheelIndex + 1);
        var compoundInfo = ResolveCompound(compound);

        DrawTireRing(canvas, leftRingX, centerY, ringRadius, leftWheel, compoundInfo);
        DrawTireRing(canvas, rightRingX, centerY, ringRadius, rightWheel, compoundInfo);

        var rowGap = axleHeight * 0.255f;
        var pressureY = centerY - rowGap;
        var tireY = centerY + valueTextSize * 0.34f;
        var brakeY = centerY + rowGap + valueTextSize * 0.34f;
        var leftValueX = innerLeft + innerWidth * 0.25f;
        var rightValueX = innerLeft + innerWidth * 0.75f;

        canvas.DrawLine(innerLeft, centerY - rowGap * 0.48f, innerRight, centerY - rowGap * 0.48f, divider);
        canvas.DrawLine(innerLeft, centerY + rowGap * 0.52f, innerRight, centerY + rowGap * 0.52f, divider);

        using var primaryText = CreateTextPaint(SKColor.Parse("#F4F7FB"), valueTextSize, true);
        using var secondaryText = CreateTextPaint(SKColor.Parse("#D8E0E9"), valueTextSize, true);
        DrawCenteredText(canvas, FormatPressure(leftWheel), leftValueX, pressureY + valueTextSize * 0.34f, primaryText);
        DrawCenteredText(canvas, FormatPressure(rightWheel), rightValueX, pressureY + valueTextSize * 0.34f, primaryText);
        DrawCenteredText(canvas, FormatTireTemperature(leftWheel), leftValueX, tireY, secondaryText);
        DrawCenteredText(canvas, FormatTireTemperature(rightWheel), rightValueX, tireY, secondaryText);

        var iconRadius = Math.Clamp(valueTextSize * 0.68f, 5.2f, 8f);
        var leftIconX = widthAwareIconX(innerLeft, innerRight, -1, iconRadius);
        var rightIconX = widthAwareIconX(innerLeft, innerRight, 1, iconRadius);
        DrawBrakeDisc(canvas, leftIconX, brakeY - valueTextSize * 0.29f, iconRadius);
        DrawBrakeDisc(canvas, rightIconX, brakeY - valueTextSize * 0.29f, iconRadius);

        var textIconGap = iconRadius + 4;
        DrawRightAlignedText(canvas, FormatBrakeTemperature(leftWheel), leftIconX - textIconGap, brakeY, secondaryText);
        DrawLeftAlignedText(canvas, FormatBrakeTemperature(rightWheel), rightIconX + textIconGap, brakeY, secondaryText);
    }

    private static float widthAwareIconX(float innerLeft, float innerRight, int direction, float radius)
    {
        var center = (innerLeft + innerRight) / 2f;
        return center + direction * (radius + 2.5f);
    }

    private WheelSample? WheelAt(int index) => index >= 0 && index < Wheels.Count ? Wheels[index] : null;

    private static void DrawTireRing(
        SKCanvas canvas,
        float x,
        float y,
        float radius,
        WheelSample? wheel,
        CompoundInfo compound)
    {
        var remaining = TireIntegrityFraction(wheel);
        var integrityColor = wheel is { Detached: true } || wheel is { Flat: true }
            ? SKColor.Parse("#F04444")
            : double.IsFinite(remaining) && remaining < 0.25
                ? SKColor.Parse("#F04444")
                : double.IsFinite(remaining) && remaining < 0.55
                    ? SKColor.Parse("#F2C94C")
                    : SKColor.Parse("#F4F7FB");
        var ringStroke = Math.Clamp(radius * 0.13f, 2f, 4.2f);
        var ringRect = new SKRect(x - radius, y - radius, x + radius, y + radius);

        using var track = new SKPaint
        {
            Color = SKColor.Parse("#314255"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ringStroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var progress = new SKPaint
        {
            Color = integrityColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ringStroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, radius, track);
        if (double.IsFinite(remaining) && remaining > 0)
            canvas.DrawArc(ringRect, -90, (float)(360 * remaining), false, progress);

        var integrityText = double.IsFinite(remaining) ? $"{remaining:P0}" : "--";
        using var integrityPaint = CreateTextPaint(integrityColor, Math.Clamp(radius * 0.53f, 8f, 16f), true);
        DrawCenteredText(canvas, integrityText, x, y - radius * 0.08f, integrityPaint);

        var badgeRadius = Math.Clamp(radius * 0.23f, 4.2f, 7f);
        var badgeY = y + radius * 0.47f;
        using var badge = new SKPaint
        {
            Color = compound.Color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, ringStroke * 0.55f),
            IsAntialias = true
        };
        canvas.DrawCircle(x, badgeY, badgeRadius, badge);
        using var compoundPaint = CreateTextPaint(compound.Color, Math.Clamp(badgeRadius * 1.18f, 6.5f, 9f), true);
        DrawCenteredText(canvas, compound.Code, x, badgeY + compoundPaint.TextSize * 0.34f, compoundPaint);
    }

    private static void DrawBrakeDisc(SKCanvas canvas, float x, float y, float radius)
    {
        using var rotor = new SKPaint
        {
            Color = SKColor.Parse("#A8B4C1"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1, radius * 0.18f),
            IsAntialias = true
        };
        using var hub = new SKPaint
        {
            Color = SKColor.Parse("#667789"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(0.8f, radius * 0.13f),
            IsAntialias = true
        };
        using var holes = new SKPaint { Color = SKColor.Parse("#7F8E9D"), IsAntialias = true };
        using var caliper = new SKPaint { Color = SKColor.Parse("#F04444"), IsAntialias = true };

        canvas.DrawCircle(x, y, radius, rotor);
        canvas.DrawCircle(x, y, radius * 0.35f, hub);
        for (var index = 0; index < 4; index++)
        {
            var angle = (45 + index * 90) * MathF.PI / 180;
            canvas.DrawCircle(
                x + MathF.Cos(angle) * radius * 0.61f,
                y + MathF.Sin(angle) * radius * 0.61f,
                Math.Max(0.65f, radius * 0.10f),
                holes);
        }
        canvas.DrawRoundRect(
            new SKRect(x + radius * 0.62f, y - radius * 0.48f, x + radius * 1.03f, y + radius * 0.48f),
            radius * 0.15f,
            radius * 0.15f,
            caliper);
    }

    private static string FormatPressure(WheelSample? wheel) =>
        wheel is not null && double.IsFinite(wheel.PressureKpa) && wheel.PressureKpa > 0
            ? $"{wheel.PressureKpa:F1}kPa"
            : "--";

    private static string FormatTireTemperature(WheelSample? wheel)
    {
        if (wheel is null) return "--";
        var surfaces = new[] { wheel.SurfaceLeftCelsius, wheel.SurfaceCenterCelsius, wheel.SurfaceRightCelsius }
            .Where(value => double.IsFinite(value) && value > 0)
            .ToArray();
        var temperature = surfaces.Length > 0 ? surfaces.Average() : wheel.CarcassTemperatureCelsius;
        return double.IsFinite(temperature) && temperature > 0 ? $"{temperature:F1}°C" : "--";
    }

    private static string FormatBrakeTemperature(WheelSample? wheel) =>
        wheel is not null && double.IsFinite(wheel.BrakeTemperatureCelsius) && wheel.BrakeTemperatureCelsius > 0
            ? $"{wheel.BrakeTemperatureCelsius:F1}°C"
            : "--";

    // The capture/import adapters normalize this field to remaining tire integrity:
    // 1 means a new tire, while 0.9 means ten percent has been worn away.
    private static double TireIntegrityFraction(WheelSample? wheel) =>
        wheel is null || !double.IsFinite(wheel.WearFraction)
            ? double.NaN
            : Math.Clamp(wheel.WearFraction, 0d, 1d);

    private static CompoundInfo ResolveCompound(string? value)
    {
        var compound = value?.Trim() ?? string.Empty;
        if (compound.Contains("soft", StringComparison.OrdinalIgnoreCase) || compound.Contains('软'))
            return new("S", SKColor.Parse("#F04444"));
        if (compound.Contains("medium", StringComparison.OrdinalIgnoreCase) || compound.Contains("中性", StringComparison.OrdinalIgnoreCase) || compound.Contains('中'))
            return new("M", SKColor.Parse("#F2D400"));
        if (compound.Contains("hard", StringComparison.OrdinalIgnoreCase) || compound.Contains('硬'))
            return new("H", SKColor.Parse("#F4F7FB"));
        if (compound.Contains("wet", StringComparison.OrdinalIgnoreCase) || compound.Contains("rain", StringComparison.OrdinalIgnoreCase) || compound.Contains('雨'))
            return new("W", SKColor.Parse("#3296FF"));

        var code = string.IsNullOrWhiteSpace(compound) ? "?" : compound[..1].ToUpperInvariant();
        return new(code, SKColor.Parse("#7B8794"));
    }

    private static SKPaint CreateTextPaint(SKColor color, float size, bool bold) => new()
    {
        Color = color,
        TextSize = size,
        IsAntialias = true,
        Typeface = SKTypeface.FromFamilyName(
            "Microsoft YaHei UI",
            bold ? SKFontStyle.Bold : SKFontStyle.Normal)
    };

    private static void DrawCenteredText(SKCanvas canvas, string text, float x, float baseline, SKPaint paint) =>
        canvas.DrawText(text, x - paint.MeasureText(text) / 2, baseline, paint);

    private static void DrawRightAlignedText(SKCanvas canvas, string text, float right, float baseline, SKPaint paint) =>
        canvas.DrawText(text, right - paint.MeasureText(text), baseline, paint);

    private static void DrawLeftAlignedText(SKCanvas canvas, string text, float left, float baseline, SKPaint paint) =>
        canvas.DrawText(text, left, baseline, paint);

    private readonly record struct CompoundInfo(string Code, SKColor Color);
}
