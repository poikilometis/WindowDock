using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    private readonly Dictionary<long, bool> enabledBeforeModal = [];
    private IReadOnlyList<NativeHoverArea> hoverAreas = [];
    private Point hostOrigin;
    private Size hostSize;
    private bool originKnown, translating;
    private void RememberHostOrigin()
    {
        if (host.Handle == 0 || Native.IsIconic(host.Handle) || !Native.GetWindowRect(host.Handle, out var rect)) return;
        hostOrigin = new(rect.Left, rect.Top); hostSize = rect.Rectangle.Size; originKnown = true;
    }
    private static Rectangle Offset(Rectangle rectangle, int dx, int dy) { rectangle.Offset(dx, dy); return rectangle; }
    private void PublishHoverTargets() => hover?.Update(hoverAreas.Select(a => new HoverTarget((nint)a.Root, (nint)a.Handle, a.Bounds, a.External)).ToArray());
    private void FollowHostTranslation()
    {
        // Runs in the native window-position message, including Windows' modal title-bar
        // move loop. No render-frame, timer or background dispatcher round-trip.
        if (translating || host.Handle == 0 || Native.IsIconic(host.Handle) || !Native.GetWindowRect(host.Handle, out var rect)) return;
        var now = new Point(rect.Left, rect.Top);
        if (!originKnown) { hostOrigin = now; hostSize = rect.Rectangle.Size; originKnown = true; return; }
        var resizing = hostSize != rect.Rectangle.Size;
        var dx = now.X - hostOrigin.X; var dy = now.Y - hostOrigin.Y;
        hostOrigin = now; hostSize = rect.Rectangle.Size;
        // Maximize/restore changes origin and size together. Moving the previous-size
        // panes first creates an unnecessary intermediate frame and another cross-process
        // positioning batch. The committed layout applies their final rectangles once.
        if (resizing) return;
        if ((dx == 0 && dy == 0) || placements.Count == 0) return;
        translating = true;
        try
        {
            placements = placements.Select(p => p with { Bounds = Offset(p.Bounds, dx, dy), Clip = Offset(p.Clip, dx, dy), Card = Offset(p.Card, dx, dy) }).ToArray();
            hoverAreas = hoverAreas.Select(a => a with { Bounds = Offset(a.Bounds, dx, dy) }).ToArray(); PublishHoverTargets();
            var visible = placements.Where(p => Native.IsWindowVisible(p.Record.Hwnd) && !Native.IsIconic(p.Record.Hwnd) && !Native.IsHungAppWindow(p.Record.Hwnd)).ToArray();
            var batch = Native.BeginWindowBatch(visible.Length);
            foreach (var placement in visible)
            {
                var handle = placement.Record.Hwnd;
                Native.GetWindowThreadProcessId(handle, out var pid); if (pid != placement.Record.ProcessId) continue;
                const uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOSIZE;
                if (batch != 0) batch = Native.MoveInBatch(batch, handle, 0, placement.Bounds.X, placement.Bounds.Y, 0, 0, flags);
                else Native.SetWindowPos(handle, 0, placement.Bounds.X, placement.Bounds.Y, 0, 0, flags);
            }
            if (batch == 0 || !Native.EndWindowBatch(batch))
                foreach (var placement in visible)
                    if (IsAlive(placement.Record)) Native.SetWindowPos(placement.Record.Hwnd, 0, placement.Bounds.X, placement.Bounds.Y, 0, 0, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOSIZE);
        }
        finally
        {
            foreach (var placement in placements) if (fitted.ContainsKey(placement.Record.Handle)) fitted[placement.Record.Handle] = placement;
            translating = false;
        }
    }
    public void RaiseOverlay(nint handle, bool clickThrough = false)
    {
        if (handle == 0 || !Native.IsWindow(handle)) return;
        var disabled = 1; Native.DwmSetWindowAttribute(handle, 3, ref disabled, 4);
        if (clickThrough) Native.SetLong(handle, Native.GWL_EXSTYLE, Native.GetLong(handle, Native.GWL_EXSTYLE) | 0x08000020L);
        Native.SetWindowPos(handle, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}

internal static partial class Native
{
    [DllImport("user32.dll", EntryPoint = "BeginDeferWindowPos", SetLastError = true)] internal static extern nint BeginWindowBatch(int count);
    [DllImport("user32.dll", EntryPoint = "DeferWindowPos", SetLastError = true)] internal static extern nint MoveInBatch(nint batch, nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "EndDeferWindowPos", SetLastError = true)] internal static extern bool EndWindowBatch(nint batch);
    [DllImport("user32.dll")] internal static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);
    [DllImport("user32.dll", EntryPoint = "EnableWindow")] internal static extern bool SetEnabled(nint window, bool enabled);
}