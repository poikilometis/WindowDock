using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Text.Json;

namespace WindowDock;

// A diagnostic surface for exercising the installed system hook with real wheel input.
// Both native editors belong to this window; no external sessions or settings are touched.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class HoverProbeWindow : Window
{
    private NativeProbeEditor? left, right;
    private HoverScroll? hover;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly TextBlock statistics = new() { Margin = new(12, 4), FontSize = 12 };
    private readonly string directory;
    public HoverProbeWindow(string directory)
    {
        this.directory = directory;
        Title = "WindowDock hover probe"; Width = 1020; Height = 600;
        Content = new StackPanel { Children = { new TextBlock { Text = "Click the left editor, then wheel over the right editor without clicking.", Margin = new(12, 6) }, statistics } };
        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()!.Handle;
            left = new(handle, 420, 450); right = new(handle, 420, 450);
            hover = new(handle); ArrangeEditors(); timer.Start();
        };
        SizeChanged += (_, _) => ArrangeEditors();
        timer.Tick += (_, _) => Report();
        Closed += (_, _) => { timer.Stop(); hover?.Dispose(); left?.Dispose(); right?.Dispose(); };
    }
    private void ArrangeEditors()
    {
        if (left is null || right is null || hover is null) return;
        var width = (int)(ClientSize.Width * RenderScaling); var height = (int)(ClientSize.Height * RenderScaling);
        var top = (int)(64 * RenderScaling); var half = width / 2;
        Native.SetWindowPos(left.Handle, 0, 8, top, half - 12, height - top - 8, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.SetWindowPos(right.Handle, 0, half + 4, top, half - 12, height - top - 8, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.GetWindowRect(left.Handle, out var a); Native.GetWindowRect(right.Handle, out var b);
        var root = TryGetPlatformHandle()!.Handle;
        hover.Update([new(root, left.Handle, a.Rectangle, false), new(root, right.Handle, b.Rectangle, false)]);
    }
    private static long Query(nint handle, uint message)
    {
        Native.SendMessageTimeout(handle, message, 0, 0, 2, 50, out var value); return value.ToInt64();
    }
    private void Report()
    {
        if (hover is null || left is null || right is null) return;
        var result = new { hover.Installed, hover.InstallError, hover.WheelEvents, hover.ForwardedEvents, hover.LastPostError, Left = left.Handle.ToInt64(), Right = right.Handle.ToInt64(), Focus = Native.ForegroundFocus().ToInt64(), LeftLine = Query(left.Handle, 0xCE), RightLine = Query(right.Handle, 0xCE), LeftWheels = Query(left.Handle, 0x8061), RightWheels = Query(right.Handle, 0x8061) };
        statistics.Text = $"Hook: {result.Installed} · input: {result.WheelEvents} · forwarded: {result.ForwardedEvents} · left line: {result.LeftLine} · right line: {result.RightLine}";
        File.WriteAllText(Path.Combine(directory, "hover-probe.json"), JsonSerializer.Serialize(result));
    }
}