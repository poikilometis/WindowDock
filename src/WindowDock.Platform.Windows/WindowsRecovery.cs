#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
namespace WindowDock { internal sealed partial class WindowsWindowBackend {
        public static bool Restore(WindowRecord r)
        {
            if (!Native.MatchesRecord(r)) return true;
            string stage = "region";
            try
            {
                stage = "styles";
                IntPtr parent = new IntPtr(r.Parent);
                if (parent != IntPtr.Zero && !Native.IsWindow(parent)) parent = IntPtr.Zero;
                if (!r.NativeTopLevel)
                {
                    // Recovery of journals written by version 1.0.
                    Native.SetLastError(0); Native.SetParent(r.Hwnd, parent);
                    if (parent == IntPtr.Zero && (Native.GetLong(r.Hwnd, Native.GWL_STYLE) & Native.WS_CHILD) != 0 &&
                        Native.GetParent(r.Hwnd) != IntPtr.Zero && Native.GetParent(r.Hwnd) != Native.GetDesktopWindow() && Native.GetParent(r.Hwnd) != new IntPtr(r.Owner))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                Native.SetLong(r.Hwnd, Native.GWL_STYLE, r.Style);
                Native.SetLong(r.Hwnd, Native.GWL_EXSTYLE, r.ExStyle);
                stage = "owner";
                if (r.NativeTopLevel || parent == IntPtr.Zero)
                {
                    var owner = r.Owner != 0 && Native.IsWindow(new IntPtr(r.Owner)) ? r.Owner : 0;
                    // Windows can clear a dead host before the guardian runs. Reassigning
                    // that already-cleared owner can report ERROR_INVALID_WINDOW_HANDLE.
                    if (Native.GetWindow(r.Hwnd, 4).ToInt64() != owner)
                        Native.SetLong(r.Hwnd, Native.GWLP_HWNDPARENT, owner);
                }
                stage = "placement";
                Rectangle original = new Rectangle(r.X, r.Y, Math.Max(160, r.Width), Math.Max(100, r.Height));
                bool visible = false;
                foreach (Rectangle area in Native.WorkAreas())
                    if (Rectangle.Intersect(area, original).Width >= 80 && Rectangle.Intersect(area, original).Height >= 50) visible = true;
                if (!visible) { Rectangle area = Native.PrimaryWorkArea(); original.Location = new Point(area.Left+40, area.Top+40); }
                IntPtr z = (r.ExStyle & Native.WS_EX_TOPMOST) != 0 ? new IntPtr(-1) : new IntPtr(-2);
                if (!Native.SetWindowPos(r.Hwnd, z, original.X, original.Y, original.Width, original.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Native.Placement p = new Native.Placement {
                    Length = Marshal.SizeOf(typeof(Native.Placement)), Flags = r.PlacementFlags, ShowCmd = r.ShowCmd,
                    Min = new Native.Point { X = r.MinX, Y = r.MinY }, Max = new Native.Point { X = r.MaxX, Y = r.MaxY },
                    Normal = new Native.Rect { Left = r.NormalLeft, Top = r.NormalTop, Right = r.NormalRight, Bottom = r.NormalBottom }
                };
                if (visible && p.Normal.Right > p.Normal.Left)
                {
                    if (!Native.SetWindowPlacement(r.Hwnd, ref p)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else Native.ShowWindowAsync(r.Hwnd, r.ShowCmd == 3 ? 3 : 5);
                stage = "region";
                if (r.NativeTopLevel) Native.RestoreRegion(r.Hwnd, r.OriginalRegion);
                if (r.Enabled.HasValue) Native.SetEnabled(r.Hwnd, r.Enabled.Value);
                return true;
            }
            catch (Exception ex) { byte[] currentRegion=Native.CaptureRegion(r.Hwnd); Store.Log("Restore " + r.ProcessName + " stage="+stage+" hwnd="+r.Handle+" live="+Native.IsWindow(r.Hwnd)+" owner="+Native.GetWindow(r.Hwnd,4)+" originalRegion="+(r.OriginalRegion==null?-1:r.OriginalRegion.Length)+" currentRegion="+(currentRegion==null?-1:currentRegion.Length)+": " + ex.Message); return false; }
        }

        public static int RecoverOrphans()
        {
            if (!Directory.Exists(Store.DirectoryPath)) return 0;
            int recovered = 0;
            foreach (string file in Directory.GetFiles(Store.DirectoryPath, "session-*.json"))
            {
                try
                {
                    Journal old = Store.Read<Journal>(file);
                    if (old == null || IsHostAlive(old.HostPid, old.HostStartTicks)) continue;
                    bool all = true;
                    if (old.Windows != null) foreach (WindowRecord r in old.Windows)
                    { bool existed = Native.MatchesRecord(r); if (Restore(r)) { if (existed) recovered++; } else all = false; }
                    if (all) File.Delete(file);
                }
                catch (Exception ex) { Store.Log("Recovery: " + ex.Message); }
            }
            return recovered;
        }
        static bool IsHostAlive(int pid, long ticks)
        {
            try { using (Process p = Process.GetProcessById(pid)) return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == ticks; } catch { return false; }
        }
        public static void Guard(int pid, long ticks, string path)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    if (p.StartTime.ToUniversalTime().Ticks == ticks) p.WaitForExit();
            } catch { }
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Journal j = Store.Read<Journal>(path);
                    if (j == null) return;
                    if (j.HostPid != pid || j.HostStartTicks != ticks) return;
                    bool all = true;
                    if (j.Windows != null) foreach (WindowRecord r in j.Windows) if (!Restore(r)) all = false;
                    if (all) { File.Delete(path); return; }
                }
                catch (Exception ex) { Store.Log("Guardian: " + ex.Message); }
                Thread.Sleep(350);
            }
        }

    }
}
