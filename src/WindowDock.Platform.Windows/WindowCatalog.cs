#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
namespace WindowDock {
    internal static class WindowCatalog
    {
        public static List<WindowCandidate> List(ICollection<long> excluded, bool includeOwn)
        {
            List<WindowCandidate> result = new List<WindowCandidate>();
            Dictionary<uint, string> images = new Dictionary<uint, string>();
            int ownPid = Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr data) {
                uint id; Native.GetWindowThreadProcessId(h, out id);
                if ((!includeOwn && id == ownPid) || !Native.IsWindowVisible(h) ||
                    (excluded != null && excluded.Contains(h.ToInt64()))) return true;
                if ((Native.GetLong(h, Native.GWL_STYLE) & Native.WS_CHILD) != 0) return true;
                long ex = Native.GetLong(h, Native.GWL_EXSTYLE);
                var owner = Native.GetWindow(h, 4);
                // Hidden helper/console owners do not make a visible application a dialog.
                if (owner != IntPtr.Zero && Native.IsWindowVisible(owner) && !Native.IsIconic(owner) &&
                    (Native.GetLong(owner, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) == 0) return true;
                string title = Native.Title(h), cls = Native.ClassName(h);
                if (String.IsNullOrWhiteSpace(title) || cls == "Progman" || cls == "WorkerW" ||
                    cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                try { int cloaked; if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true; } catch { }
                string name; long processStart=0;
                try { using (Process p = Process.GetProcessById((int)id)) { name = p.ProcessName; try { processStart=p.StartTime.ToUniversalTime().Ticks; } catch { } } } catch { name = L10n.Get("RuleDialogs.Application"); }
                bool term = cls == Native.ConsoleWindowClass || cls == Native.TerminalWindowClass ||
                    name.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Equals("powershell", StringComparison.OrdinalIgnoreCase) || name.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("cmd", StringComparison.OrdinalIgnoreCase) || name.Equals("conhost", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("wezterm-gui", StringComparison.OrdinalIgnoreCase) || name.Equals("mintty", StringComparison.OrdinalIgnoreCase);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !term) return true;
                if (!images.TryGetValue(id, out var image)) images[id] = image = WindowsImages.Path(id);
                result.Add(new WindowCandidate { Handle = h, ProcessId = (int)id, ProcessStartTicks = processStart, Title = title,
                    ProcessName = name, ClassName = cls, ExecutablePath = image, IsTerminal = term, IsMinimized = Native.IsIconic(h) });
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowCandidate a, WindowCandidate b) {
                int order = b.IsTerminal.CompareTo(a.IsTerminal);
                return order != 0 ? order : String.Compare(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }
    }
}
