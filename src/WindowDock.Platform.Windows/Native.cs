#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowDock
{
    internal static partial class Native
    {
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        public const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L, WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L, WS_SYSMENU = 0x00080000L, WS_MINIMIZEBOX = 0x20000L, WS_MAXIMIZEBOX = 0x10000L;
        public const long WS_CLIPCHILDREN = 0x02000000L, WS_CLIPSIBLINGS = 0x04000000L;
        public const long WS_EX_APPWINDOW = 0x40000L, WS_EX_TOOLWINDOW = 0x80L, WS_EX_TOPMOST = 0x8L;
        public const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_ASYNCWINDOWPOS = 0x4000;
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll")] public static extern bool FreeConsole();
        [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(IntPtr handler,bool add);
        [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string text);
        [StructLayout(LayoutKind.Sequential)] public struct CopyData { public IntPtr Tag; public int Size; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct ProcessEntry
        {
            public uint Size, Usage, ProcessId;
            public UIntPtr DefaultHeap;
            public uint ModuleId, Threads, ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string ExeFile;
        }
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] public struct Placement
        {
            public int Length, Flags, ShowCmd;
            public Point Min, Max;
            public Rect Normal;
        }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)] static extern IntPtr GetLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] static extern IntPtr SetLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW", SetLastError=true)] static extern int GetLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW", SetLastError=true)] static extern int SetLong32(IntPtr hwnd, int index, int value);
        [DllImport("kernel32.dll")] public static extern void SetLastError(uint code);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
        [DllImport("user32.dll", SetLastError=true)] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] public static extern uint GetRegionData(IntPtr region, uint size, byte[] data);
        [DllImport("gdi32.dll")] public static extern IntPtr ExtCreateRegion(IntPtr transform, uint size, byte[] data);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern int GetRgnBox(IntPtr region, out Rect box);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr data);
        public static bool HasRectRegion(IntPtr hwnd, Rectangle expected)
        {
            IntPtr region=CreateRectRgn(0,0,0,0);
            try
            {
                Rect box;
                return GetWindowRgn(hwnd,region)==2 && GetRgnBox(region,out box)!=0 && box.Rectangle==expected;
            }
            finally { DeleteObject(region); }
        }
        public static List<IntPtr> ChildWindows(IntPtr hwnd)
        {
            List<IntPtr> children=new List<IntPtr>();
            EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr unused){children.Add(child); return true;},IntPtr.Zero);
            return children;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct GuiThreadInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        public static IntPtr ForegroundFocus()
        {
            GuiThreadInfo info=new GuiThreadInfo(); info.Size=Marshal.SizeOf(typeof(GuiThreadInfo));
            return GetGUIThreadInfo(0,ref info)?info.Focus:GetFocus();
        }
        public static byte[] CaptureRegion(IntPtr hwnd)
        {
            IntPtr region=CreateRectRgn(0,0,0,0);
            try
            {
                if(GetWindowRgn(hwnd,region)==0) return null;
                uint size=GetRegionData(region,0,null);
                if(size==0 || size>4*1024*1024) return null;
                byte[] data=new byte[size];
                return GetRegionData(region,size,data)==size?data:null;
            }
            finally { DeleteObject(region); }
        }
        public static void RestoreRegion(IntPtr hwnd,byte[] data)
        {
            // Some native frameworks clear their region themselves while restoring frame styles.
            // Removing an already absent region is unnecessary.
            if(data==null && CaptureRegion(hwnd)==null) return;
            IntPtr region=data==null?IntPtr.Zero:ExtCreateRegion(IntPtr.Zero,(uint)data.Length,data);
            if(data!=null && region==IntPtr.Zero) throw new InvalidOperationException(L10n.Get("Native.CouldNotRestoreTheWindowRegion"));
            SetLastError(0);
            if(SetWindowRgn(hwnd,region,true)==0)
            {
                int error=Marshal.GetLastWin32Error();
                if(region!=IntPtr.Zero) DeleteObject(region);
                if(!IsWindow(hwnd) || (data==null && CaptureRegion(hwnd)==null)) return;
                throw new System.ComponentModel.Win32Exception(error,L10n.Get("Native.CouldNotRestoreTheWindowRegion"));
            }
        }

        public delegate IntPtr MouseHookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] public struct MouseHookData
        {
            public Point Position;
            public uint MouseData, Flags, Time;
            public UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] public struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id, MouseHookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetKeyState(int key);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, Point point, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest, IntPtr first, IntPtr second, int mode);
        public static bool HasRegionBox(IntPtr hwnd, Rectangle expected)
        {
            IntPtr region=CreateRectRgn(0,0,0,0);
            try { Rect box; return GetWindowRgn(hwnd,region)>0 && GetRgnBox(region,out box)!=0 && box.Rectangle==expected; }
            finally { DeleteObject(region); }
        }
        public static IntPtr DeepWindowAt(Point screenPoint)
        {
            IntPtr window=WindowFromPoint(screenPoint);
            for(int i=0;i<16 && window!=IntPtr.Zero;i++)
            {
                Point local=screenPoint;
                if(!ScreenToClient(window,ref local)) break;
                IntPtr child=ChildWindowFromPointEx(window,local,7);
                if(child==IntPtr.Zero || child==window) break;
                window=child;
            }
            return window;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)]
        public struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public MouseInput Mouse; }
        [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);

        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd,string app,string ids);

        public static long GetLong(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetLong64(hwnd, index).ToInt64() : (long)(uint)GetLong32(hwnd, index);
        }
        public static void SetLong(IntPtr hwnd, int index, long value)
        {
            SetLastError(0);
            IntPtr previous = IntPtr.Size == 8
                ? SetLong64(hwnd, index, new IntPtr(value))
                : new IntPtr(SetLong32(hwnd, index, unchecked((int)value)));
            int error = Marshal.GetLastWin32Error();
            // GetLastError is meaningful only when the API's return value is zero.
            // Successful frame / owner changes can leave an error from nested native calls.
            if (previous == IntPtr.Zero && error != 0) throw new System.ComponentModel.Win32Exception(error, $"SetWindowLongPtr index={index} requested={value} current={GetLong(hwnd, index)}: {new System.ComponentModel.Win32Exception(error).Message}");
        }
        public static string Title(IntPtr hwnd)
        {
            StringBuilder b = new StringBuilder(1024); GetWindowText(hwnd, b, b.Capacity); return b.ToString();
        }
        public static string ClassName(IntPtr hwnd)
        {
            StringBuilder b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString();
        }
        public static void DarkTitle(IntPtr hwnd)
        {
            try { int yes = 1; DwmSetWindowAttribute(hwnd, 20, ref yes, 4); } catch { }
        }
        public static void FocusExternal(IntPtr hwnd)
        {
            if (!IsWindow(hwnd)) return;
            uint pid; uint other = GetWindowThreadProcessId(hwnd, out pid), current = GetCurrentThreadId();
            bool joined = other != current && AttachThreadInput(current, other, true);
            try { SetFocus(hwnd); } finally { if (joined) AttachThreadInput(current, other, false); }
        }
    }

}
