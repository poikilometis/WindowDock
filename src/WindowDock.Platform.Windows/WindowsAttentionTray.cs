using System.Runtime.InteropServices;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    private WindowsAttentionTray? attentionTray;

    internal void ConfigureAgentTray(bool enabled, string tooltip)
    {
        if (enabled)
        {
            attentionTray ??= new(host.Handle, () => Changed?.Invoke(new("agent-tray", 0)));
            attentionTray.Update(tooltip);
        }
        else
        {
            attentionTray?.Dispose();
            attentionTray = null;
        }
    }

    internal void NotifyAgent(string message)
    {
        if (attentionTray?.Notify(message) == false)
            Store.Log("Windows did not accept a WindowDock agent notification.");
    }
}

internal sealed class WindowsAttentionTray(nint owner, Action open) : IDisposable
{
    private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, InfoFlag = 0x10, ShowTip = 0x80;
    private const uint CallbackMessage = 0x80B1, IconId = 1;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private string tip = "WindowDock";
    private bool registered;

    internal void Update(string tooltip)
    {
        var changed = tip != tooltip;
        tip = tooltip;
        if (!registered) Register();
        else if (changed)
        {
            var data = Data(TipFlag | ShowTip);
            ShellNotifyIcon(Modify, ref data);
        }
    }

    internal bool Notify(string message)
    {
        if (!registered) Register();
        if (!registered) return false;
        var data = Data(InfoFlag);
        data.Title = "WindowDock";
        data.Info = ClampText(message, 255);
        data.InfoFlags = 0x11; // NIIF_INFO | NIIF_NOSOUND
        if (ShellNotifyIcon(Modify, ref data)) return true;
        // Explorer may have restarted before the tray recreation message reached this window.
        registered = false;
        Register();
        return registered && ShellNotifyIcon(Modify, ref data);
    }

    internal bool HandleMessage(uint message, nuint wParam, nint lParam)
    {
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            registered = false;
            Register();
            return false;
        }
        if (message != CallbackMessage) return false;
        var packed = unchecked((uint)lParam);
        if ((packed >> 16) == IconId && (packed & 0xFFFF) is 0x400 or 0x401 or 0x405 or 0x007B)
            open(); // NIN_SELECT, NIN_KEYSELECT, NIN_BALLOONUSERCLICK, WM_CONTEXTMENU
        return true;
    }

    private void Register()
    {
        var data = Data(MessageFlag | IconFlag | TipFlag | ShowTip);
        data.Callback = CallbackMessage;
        data.Icon = SendMessage(owner, 0x007F, 0, 0); // WM_GETICON, ICON_SMALL
        if (data.Icon == 0) data.Icon = LoadIcon(0, 32512); // IDI_APPLICATION
        registered = ShellNotifyIcon(Add, ref data);
        if (!registered) return;
        data.Version = 4;
        ShellNotifyIcon(SetVersion, ref data);
    }

    private NotificationData Data(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotificationData>(),
        Owner = owner,
        Id = IconId,
        Flags = flags,
        Tip = ClampText(tip, 127),
        Info = "",
        Title = ""
    };

    private static string ClampText(string value, int length)
    {
        if (value.Length <= length) return value;
        return value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)];
    }

    public void Dispose()
    {
        if (!registered) return;
        var data = Data(0);
        ShellNotifyIcon(Delete, ref data);
        registered = false;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation, ref NotificationData data);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern nint LoadIcon(nint instance, nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotificationData
    {
        internal uint Size;
        internal nint Owner;
        internal uint Id, Flags, Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags;
        internal Guid Guid;
        internal nint BalloonIcon;
    }
}
