using System.Runtime.InteropServices;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    private WindowsAttentionTray? attentionTray;
    internal WindowsAttentionTray.IconOperation? TestTrayOperation { get; set; }

    internal void ConfigureAgentTray(bool enabled, string tooltip)
    {
        if (enabled)
        {
            attentionTray ??= new(host.Handle, () => Changed?.Invoke(new("agent-tray", 0)), TestTrayOperation);
            attentionTray.Update(tooltip);
        }
        else
        {
            attentionTray?.Dispose();
            attentionTray = null;
        }
    }

    internal void NotifyAgent(string message, Action activate)
    {
        if (attentionTray?.Notify(message, activate) == false)
            Store.Log("Windows did not accept a WindowDock agent notification.");
    }
}

internal sealed class WindowsAttentionTray(nint owner, Action open, WindowsAttentionTray.IconOperation? operation = null) : IDisposable
{
    private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, StateFlag = 8, InfoFlag = 0x10, ShowTip = 0x80;
    private const uint TrayIconId = 1, Hidden = 1;
    private const int NotificationLimit = 64;
    internal const uint CallbackMessage = 0x80B1, BalloonUserClick = 0x405;
    internal delegate bool IconOperation(uint operation, ref NotificationData data);

    private readonly IconOperation send = operation ?? ShellNotifyIcon;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly Dictionary<uint, (long Sequence, Action Activate)> notifications = [];
    private long nextSequence;
    private string tip = "WindowDock";
    private bool registered;

    internal void Update(string tooltip)
    {
        var changed = tip != tooltip;
        tip = tooltip;
        if (!registered) registered = Register(TrayIconId);
        else if (changed)
        {
            var data = Data(TrayIconId, TipFlag | ShowTip);
            send(Modify, ref data);
        }
    }

    internal bool Notify(string message, Action activate)
    {
        // A Windows balloon callback identifies its icon only. A separate hidden icon
        // keeps queued notifications clickable without sharing the tray button's target.
        uint id;
        long sequence;
        do
        {
            sequence = nextSequence++;
            id = 2 + (uint)(sequence % (ushort.MaxValue - 1));
        } while (notifications.ContainsKey(id));

        var data = Data(id, InfoFlag);
        data.Title = "WindowDock";
        data.Info = ClampText(message, 255);
        data.InfoFlags = 0x11; // NIIF_INFO | NIIF_NOSOUND
        if (!ShowBalloon(ref data, sequence, activate) && !ShowBalloon(ref data, sequence, activate)) return false;
        if (notifications.Count > NotificationLimit)
            RemoveNotification(notifications.MinBy(pair => pair.Value.Sequence).Key);
        return true;
    }

    private bool ShowBalloon(ref NotificationData data, long sequence, Action activate)
    {
        if (!Register(data.Id, hidden: true)) return false;
        notifications.Add(data.Id, (sequence, activate));
        if (send(Modify, ref data)) return true;
        RemoveNotification(data.Id);
        return false;
    }

    internal bool HandleMessage(uint message, nint lParam)
    {
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            notifications.Clear();
            registered = Register(TrayIconId);
            return false;
        }
        if (message != CallbackMessage) return false;
        var packed = unchecked((uint)lParam);
        var id = packed >> 16;
        var notification = packed & 0xFFFF;
        if (id == TrayIconId && notification is 0x400 or 0x401 or 0x007B)
            open(); // NIN_SELECT, NIN_KEYSELECT, WM_CONTEXTMENU
        else if (notification == BalloonUserClick && notifications.TryGetValue(id, out var target))
        {
            RemoveNotification(id);
            target.Activate();
        }
        return true;
    }

    private bool Register(uint id, bool hidden = false)
    {
        var data = Data(id, MessageFlag | IconFlag | (hidden ? StateFlag : TipFlag | ShowTip));
        data.Callback = CallbackMessage;
        data.Icon = SendMessage(owner, 0x007F, 0, 0); // WM_GETICON, ICON_SMALL
        if (data.Icon == 0) data.Icon = LoadIcon(0, 32512); // IDI_APPLICATION
        if (hidden) data.State = data.StateMask = Hidden;
        if (!send(Add, ref data)) return false;
        data.Version = 4;
        if (send(SetVersion, ref data)) return true;
        DeleteIcon(id);
        return false;
    }

    private void RemoveNotification(uint id)
    {
        notifications.Remove(id);
        DeleteIcon(id);
    }

    private void DeleteIcon(uint id)
    {
        var data = Data(id, 0);
        send(Delete, ref data);
    }

    private NotificationData Data(uint id, uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotificationData>(),
        Owner = owner,
        Id = id,
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
        foreach (var id in notifications.Keys.ToArray()) RemoveNotification(id);
        if (registered) DeleteIcon(TrayIconId);
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
    internal struct NotificationData
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
