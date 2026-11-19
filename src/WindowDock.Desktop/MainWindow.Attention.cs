using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private Popup? agentActivityPopup;
    private AgentActivityCard? agentActivityCard;
    private bool waitingAgentsOnly;

    private void ToggleAgentActivity()
    {
        if (closing || (dialogDepth > 0 && ActiveMenu is null)) return;
        if (agentActivityPopup is { IsOpen: true }) { CloseAgentActivity(focusBell: true); return; }
        ActiveMenu?.Close();
        RestoreHost();
        var card = new AgentActivityCard(Session, waitingAgentsOnly, () => CloseAgentActivity(focusBell: true),
            activity => _ = UserActionAsync(() => OpenAgentActivityAsync(activity)));
        var popup = new Popup
        {
            Child = card, PlacementTarget = toolbar, Placement = PlacementMode.BottomEdgeAlignedRight,
            HorizontalOffset = -8, VerticalOffset = 8, IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = true, OverlayInputPassThroughElement = attention,
            ShouldUseOverlayLayer = false
        };
        agentActivityPopup = popup;
        agentActivityCard = card;
        ResizeAgentActivity();
        popup.Opened += (_, _) =>
        {
            if (TopLevel.GetTopLevel(card)?.TryGetPlatformHandle() is { } handle &&
                handle.Handle != TryGetPlatformHandle()?.Handle) Session.Backend.RaiseOverlay(handle.Handle);
            Dispatcher.UIThread.Post(() => { if (popup.IsOpen) card.FocusContent(); }, DispatcherPriority.Loaded);
        };
        popup.Closed += (_, _) =>
        {
            waitingAgentsOnly = card.WaitingOnly;
            root.Children.Remove(popup);
            if (ReferenceEquals(agentActivityPopup, popup)) { agentActivityPopup = null; agentActivityCard = null; }
        };
        root.Children.Add(popup);
        popup.Open();
    }

    private void ResizeAgentActivity()
    {
        if (agentActivityCard is { } card) card.MaxHeight = Math.Clamp(ClientSize.Height - toolbar.Bounds.Height - 16, 144, 380);
    }

    private void CloseAgentActivity(bool focusBell = false)
    {
        agentActivityPopup?.Close();
        if (focusBell) attention.Focus();
    }

    private async Task OpenAgentActivityAsync(AgentActivity selected)
    {
        CloseAgentActivity();
        if (selected.Panel is { } panel && Session.Panels.Contains(panel) && Session.Backend.IsAlive(panel.Record))
        {
            lastAgentKey = selected.Key;
            await ActivateAgentAsync(panel, selected.Key);
        }
        else if (Session.HookSessions.ContainsKey(selected.Key))
        {
            var picker = new PanelPicker(Session);
            if (!await ShowEditorAsync(picker) || picker.Selected is not { } destination) return;
            Session.BindHook(selected.Key, destination);
            lastAgentKey = selected.Key;
            await ActivateAgentAsync(destination, selected.Key);
        }
    }
}
