using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WindowDock;

internal sealed class BeamsView : Control
{
    private const int PulseLevels = 13;
    private static readonly Beam[] beams = CreateBeams();
    private static readonly RadialGradientBrush shade = new()
    {
        Center = RelativePoint.Center,
        GradientOrigin = RelativePoint.Center,
        RadiusX = new(.58, RelativeUnit.Relative),
        RadiusY = new(.58, RelativeUnit.Relative),
        GradientStops =
        [
            new(Color.FromArgb(137, 9, 12, 21), 0),
            new(Color.FromArgb(82, 9, 12, 21), .42),
            new(Color.FromArgb(0, 9, 12, 21), 1)
        ]
    };
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(42) };

    internal BeamsView()
    {
        IsHitTestVisible = false;
        timer.Tick += (_, _) => InvalidateVisual();
    }

    internal bool AnimationRunning => timer.IsEnabled;

    internal void SetRunning(bool visible)
    {
        if (visible && (!OperatingSystem.IsWindows() || Native.ClientAnimationsEnabled()))
        {
            clock.Start();
            timer.Start();
        }
        else
        {
            timer.Stop();
            clock.Stop();
        }
    }

    public override void Render(DrawingContext context) => Draw(context, new Rect(Bounds.Size), clock.Elapsed.TotalSeconds);

    internal static void Draw(DrawingContext context, Rect bounds, double seconds = 0)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var length = Math.Max(bounds.Height * 2.1, bounds.Width * .45);
        foreach (var beam in beams)
        {
            var width = Math.Min(bounds.Width * beam.Width, length * .30);
            var extent = (length * Math.Cos(beam.Angle) + width * Math.Abs(Math.Sin(beam.Angle))) / 2;
            var progress = (beam.Phase + seconds * beam.Speed) % 1;
            var y = bounds.Bottom + extent - progress * (bounds.Height + 2 * extent);
            var pulse = Math.Sin(seconds * .65 + beam.Phase * Math.Tau);
            var x = bounds.X + bounds.Width * beam.Position + width * .12 * pulse;

            // Cached soft gradients avoid a blur or offscreen opacity layer for each beam.
            var level = (int)Math.Round((pulse + 1) * (PulseLevels - 1) / 2);
            using var transform = context.PushTransform(Matrix.CreateRotation(beam.Angle) * Matrix.CreateTranslation(x, y));
            context.DrawRectangle(beam.Fills[level], null, new Rect(-width / 2, -length / 2, width, length));
        }
        context.DrawRectangle(shade, null, bounds);
    }

    private static Beam[] CreateBeams()
    {
        var colors = new[] { Color.Parse("#42CDE9"), Color.Parse("#4B9CF8"), Color.Parse("#616EFF"), Color.Parse("#9169FB") };
        var brushes = colors.Select(color =>
        {
            GradientStops stops =
            [
                new(Color.FromArgb(144, color.R, color.G, color.B), 0),
                new(Color.FromArgb(115, color.R, color.G, color.B), .29),
                new(Color.FromArgb(46, color.R, color.G, color.B), .67),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            ];
            return Enumerable.Range(0, PulseLevels).Select(level => new RadialGradientBrush
            {
                Center = RelativePoint.Center,
                GradientOrigin = RelativePoint.Center,
                RadiusX = new(.5, RelativeUnit.Relative),
                RadiusY = new(.5, RelativeUnit.Relative),
                Opacity = .62 + .36 * level / (PulseLevels - 1),
                GradientStops = stops
            }).ToArray();
        }).ToArray();
        var random = new Random(35);
        return Enumerable.Range(0, 14).Select(index => new Beam(
            (index + .5) / 14 + (random.NextDouble() - .5) * .14,
            .17 + random.NextDouble() * .66,
            .11 + random.NextDouble() * .16,
            .009 + random.NextDouble() * .008,
            (-35 + random.NextDouble() * 10) * Math.PI / 180,
            brushes[index % brushes.Length])).ToArray();
    }

    private sealed record Beam(double Position, double Phase, double Width, double Speed, double Angle, IBrush[] Fills);
}
