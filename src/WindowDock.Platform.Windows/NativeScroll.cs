using System.Runtime.InteropServices;
namespace WindowDock;
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct ScrollInformation
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint parameter, out uint value, uint flags);
    internal static uint WheelScrollLines() => SystemParametersInfo(0x0068, 0, out var lines, 0) ? lines : 3;
    [DllImport("user32.dll")] private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInformation information);
    internal static bool VerticalScroll(nint window, out ScrollInformation information)
    {
        information = new() { Size = (uint)Marshal.SizeOf<ScrollInformation>(), Mask = 7 };
        return GetScrollInfo(window, 1, ref information);
    }
}