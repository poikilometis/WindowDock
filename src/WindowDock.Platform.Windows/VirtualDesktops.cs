using System.Runtime.InteropServices;

namespace WindowDock;

internal static class VirtualDesktops
{
    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    private interface IManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }

    internal static Guid? Desktop(nint window) => Use<Guid?>(null,
        manager => manager.GetWindowDesktopId(window, out var id) == 0 && id != Guid.Empty ? id : null);

    internal static bool OnCurrent(nint window) => Use(true,
        manager => manager.IsWindowOnCurrentVirtualDesktop(window, out var current) != 0 || current);

    internal static bool Move(nint window, Guid desktop) => Use(false, manager => manager.MoveWindowToDesktop(window, in desktop) == 0);

    private static T Use<T>(T fallback, Func<IManager, T> action)
    {
        IManager? manager = null;
        try
        {
            manager = (IManager?)Activator.CreateInstance(Type.GetTypeFromCLSID(ManagerClass)!);
            return manager is null ? fallback : action(manager);
        }
        catch (COMException) { return fallback; }
        finally { if (manager is not null) Marshal.ReleaseComObject(manager); }
    }
}
