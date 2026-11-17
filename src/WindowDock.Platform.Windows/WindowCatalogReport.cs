using System.Diagnostics;
using System.Text.Json;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    // Explicit, read-only support diagnostic. Never attaches, restores or activates an app.
    public static void WriteCatalogReport(string path)
    {
        var eligible = WindowCatalog.List(null, false).ToDictionary(c => c.Handle.ToInt64());
        List<object> rows = [];
        Native.EnumWindows((window, _) =>
        {
            var title = Native.Title(window); if (!Native.IsWindowVisible(window) || string.IsNullOrWhiteSpace(title)) return true;
            Native.GetWindowThreadProcessId(window, out var pid); var owner = Native.GetWindow(window, 4);
            var processName = ""; try { using var process = Process.GetProcessById((int)pid); processName = process.ProcessName; } catch { }
            var cloaked = 0; Native.DwmGetWindowAttribute(window, 14, out cloaked, 4);
            rows.Add(new { Handle = window.ToInt64(), ProcessId = pid, ProcessName = processName, Title = title, ClassName = Native.ClassName(window), Minimized = Native.IsIconic(window), Enabled = Native.IsWindowEnabled(window), ToolWindow = (Native.GetLong(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0, Owner = owner.ToInt64(), OwnerTitle = Native.Title(owner), OwnerVisible = owner != 0 && Native.IsWindowVisible(owner), OwnerToolWindow = owner != 0 && (Native.GetLong(owner, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0, Cloaked = cloaked, Eligible = eligible.ContainsKey(window.ToInt64()), NativeScroll = Native.VerticalScroll(window, out var scroll) ? new { scroll.Minimum, scroll.Maximum, scroll.Page, scroll.Position } : null, Children = Native.ChildWindows(window).Select(child => new { Handle = child.ToInt64(), ClassName = Native.ClassName(child), Visible = Native.IsWindowVisible(child), Enabled = Native.IsWindowEnabled(child), ExStyle = Native.GetLong(child, Native.GWL_EXSTYLE) }).ToArray() });
            return true;
        }, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}