using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WindowDock;

internal sealed class NotificationHighlight : Control
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(34);
    private static readonly KeySpline fade = new(.23, 1, .32, 1);
    private readonly DispatcherTimer timer = new();
    private readonly ConicGradientBrush gradient = new()
    {
        Center = RelativePoint.Center,
        GradientStops =
        [
            new(Color.Parse("#00CCB1"), 0),
            new(Color.Parse("#1CA0FB"), .25),
            new(Color.Parse("#7B61FF"), .5),
            new(Color.Parse("#FFC414"), .75),
            new(Color.Parse("#00CCB1"), 1)
        ]
    };
    private readonly Pen edge, glow;
    private long startedAt;
    private double firstAngle, firstOpacity;
    private bool animate;

    internal NotificationHighlight()
    {
        IsHitTestVisible = false;
        IsVisible = false;
        Margin = new Thickness(-2);
        edge = new(gradient, 2.6);
        glow = new(gradient, 6);
        timer.Tick += (_, _) => UpdateFrame();
    }

    internal void Play()
    {
        animate = !OperatingSystem.IsWindows() || Native.ClientAnimationsEnabled();
        firstAngle = gradient.Angle;
        firstOpacity = IsVisible ? Opacity : .64;
        startedAt = Stopwatch.GetTimestamp();
        Opacity = animate ? firstOpacity : 1;
        IsVisible = true;
        timer.Stop();
        timer.Interval = animate ? FrameInterval : Duration;
        timer.Start();
    }

    internal void Stop()
    {
        timer.Stop();
        IsVisible = false;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateFrame()
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        if (!animate || !IsEffectivelyVisible || elapsed >= Duration ||
            TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Minimized })
        {
            Stop();
            return;
        }

        var seconds = elapsed.TotalSeconds;
        var enter = fade.GetSplineProgress(Math.Min(1, seconds / .14));
        var exit = fade.GetSplineProgress(Math.Clamp((seconds - 3.78) / .22, 0, 1));
        Opacity = (firstOpacity + (1 - firstOpacity) * enter) * (1 - exit);
        gradient.Angle = (firstAngle + 90 * seconds) % 360;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 4 || Bounds.Height <= 4) return;
        var bounds = new Rect(Bounds.Size).Deflate(1.3);
        using (context.PushOpacity(.22))
            context.DrawRectangle(null, glow, bounds, 7, 7);
        context.DrawRectangle(null, edge, bounds, 7, 7);
    }
}
