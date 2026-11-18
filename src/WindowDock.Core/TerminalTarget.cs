using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed class TerminalTarget
{
    [DataMember] public string Key = "", Tab = "";
    [DataMember(EmitDefaultValue = false)] public int Pane;
    internal bool Valid => Key is { Length: > 0 and <= 128 } && Tab is { Length: > 0 and <= 256 } &&
        !Tab.Contains('\0') && Pane is >= 0 and <= 32;
}

internal sealed partial class WorkspaceSession
{
    internal void SaveTerminalTarget(RuntimePanel panel, TerminalTarget target)
    {
        if (!target.Valid || !Panels.Contains(panel) || !panel.HookSessions.Contains(target.Key)) return;
        panel.TerminalTargets.RemoveAll(item => item.Key == target.Key);
        if (panel.TerminalTargets.Count >= 25) panel.TerminalTargets.RemoveAt(0);
        panel.TerminalTargets.Add(target);
        RequestSave();
    }
}
