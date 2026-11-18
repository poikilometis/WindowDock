using System.ComponentModel;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    private readonly HashSet<nint> workspaceHosts = [];
    private readonly Dictionary<long, nint> paneOwners = [];

    private nint OwnerOf(WindowRecord record) => paneOwners.GetValueOrDefault(record.Handle, host.Handle);

    internal bool RegisterWorkspaceHost(nint window)
    {
        if (window == 0 || disposed || !initialized) return false;
        if (workspaceHosts.Contains(window)) return true;
        if (!SetWindowSubclass(window, subclass!, 0x57444F43, 0)) return false;
        workspaceHosts.Add(window);
        RememberHostOrigin(window);
        return true;
    }

    internal bool ReleaseWorkspaceHost(nint window)
    {
        if (!workspaceHosts.Contains(window)) return true;
        foreach (var record in windows.Where(record => !record.Linked && OwnerOf(record) == window && IsAlive(record)))
            if (!ChangeOwner(record, host.Handle)) return false;
        workspaceHosts.Remove(window);
        hostGeometry.Remove(window);
        RemoveWindowSubclass(window, subclass!, 0x57444F43);
        return true;
    }

    private bool ChangeOwner(WindowRecord record, nint target)
    {
        if (OwnerOf(record) == target) return true;
        if (Native.IsHungAppWindow(record.Hwnd)) return false;
        try
        {
            Hide(record);
            Native.SetLong(record.Hwnd, Native.GWLP_HWNDPARENT, target.ToInt64());
            if (Native.GetWindow(record.Hwnd, 4) != target) return false;
            if (target == host.Handle) paneOwners.Remove(record.Handle); else paneOwners[record.Handle] = target;
            fitted.Remove(record.Handle);
            clips.Remove(record.Handle);
            return true;
        }
        catch (Win32Exception exception) { Store.Log(exception.Message); return false; }
    }
}
