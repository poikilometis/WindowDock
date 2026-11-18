using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace WindowDock;

internal sealed class SurfaceBrushes : IDisposable
{
    private readonly Dictionary<(string? Fill, Color Start, Color End, int Opacity), IBrush> cache = [];
    private readonly List<ConicGradientBrush> flowing = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(42) };
    private readonly Stopwatch clock = new();
    private bool running;

    internal SurfaceBrushes() => timer.Tick += (_, _) =>
    {
        var angle = clock.Elapsed.TotalSeconds * 32 % 360;
        foreach (var brush in flowing) brush.Angle = angle;
    };

    internal IBrush Get(SurfaceStyle style, IBrush fallback, int opacity = 100)
    {
        if (!style.HasFill && opacity == 100) return fallback;
        var start = style.HasFill && style.StartColor is { } color ? Color.FromUInt32(color) : ((ISolidColorBrush)fallback).Color;
        var end = style.EndColor is { } second ? Color.FromUInt32(second) : ((ISolidColorBrush)DockTheme.Accent).Color;
        var key = (style.Fill, start, end, opacity);
        if (cache.TryGetValue(key, out var brush)) return brush;
        brush = style.Fill switch
        {
            "gradient" => Gradient(start, end, opacity / 100d),
            "flow" => Flow(start, end, opacity / 100d),
            _ => new SolidColorBrush(start, opacity / 100d)
        };
        cache.Add(key, brush);
        return brush;
    }

    internal static bool IsTranslucent(SurfaceStyle style) => style.OpacityPercent() < 100 ||
        (style.HasFill && style.StartColor is { } start && start >> 24 < 255) ||
        (style.Fill is "gradient" or "flow" && style.EndColor is { } end && end >> 24 < 255);

    private static LinearGradientBrush Gradient(Color start, Color end, double opacity)
    {
        var brush = ColorPickerView.Gradient((0, start), (1, end));
        brush.Opacity = opacity;
        return brush;
    }

    private ConicGradientBrush Flow(Color start, Color end, double opacity)
    {
        var brush = new ConicGradientBrush
        {
            Center = RelativePoint.Center, Opacity = opacity, Angle = clock.Elapsed.TotalSeconds * 32 % 360,
            GradientStops = [new(start, 0), new(end, .5), new(start, 1)]
        };
        flowing.Add(brush);
        UpdateTimer();
        return brush;
    }

    internal void SetRunning(bool value)
    {
        running = value && (!OperatingSystem.IsWindows() || Native.ClientAnimationsEnabled());
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (running && flowing.Count > 0) { clock.Start(); timer.Start(); }
        else { timer.Stop(); clock.Stop(); }
    }

    internal void Reset()
    {
        cache.Clear();
        flowing.Clear();
        UpdateTimer();
    }

    public void Dispose() { running = false; Reset(); }
}
