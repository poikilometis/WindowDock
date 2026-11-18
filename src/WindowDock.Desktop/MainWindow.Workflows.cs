using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DrawingSize = System.Drawing.Size;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly TextBlock attentionCount = new() { FontSize = 11, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border stackBar = new() { IsVisible = false };
    private readonly TextBlock stackLabel = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 320 };
    private bool attentionUpdateQueued;
    private RuntimePanel? pendingActivation;
    private string? lastAgentKey;
    private WindowState lastVisibleState = WindowState.Normal;
    private HashSet<int> unavailableShortcuts = [];

    private void OpenWorkspace(WorkspaceConfig workspace)
    {
        if (Session.ActivePanel(workspace) is { } panel)
            ActivatePanel(panel);
        else
        {
            pendingActivation = null;
            Session.SelectedWorkspace = workspace.Id;
            RestoreHost();
        }
    }

    internal void ActivatePanel(RuntimePanel panel)
    {
        if (!Session.Panels.Contains(panel) || !Session.Backend.IsAlive(panel.Record)) return;
        RestoreHost();
        Session.SelectedWorkspace = panel.WorkspaceId;
        if (Session.FocusedPanel is { } focus && focus != panel.Id) Session.FocusedPanel = null;
        if (Session.Current.Layout is "stack" or "adaptive")
        {
            var pinIsVisible = Session.Current.PinnedPanelId == panel.Id && stackBar.IsVisible &&
                cards.TryGetValue(panel.Id, out var pinned) && pinned.IsVisible;
            if (!pinIsVisible) Session.SelectStackPanel(panel);
        }
        Session.RememberActivation(panel);
        pendingActivation = panel;
        if (stackBar.IsVisible || Session.Current.Layout is "stack" or "adaptive") ArrangePanels();
        Dispatcher.UIThread.Post(() =>
        {
            if (closing || !ReferenceEquals(pendingActivation, panel)) return;
            if (cards.TryGetValue(panel.Id, out var card) && card.IsVisible) card.BringIntoView();
            QueueSync();
        }, DispatcherPriority.Loaded);
    }

    private void CompletePanelActivation(IReadOnlyList<WindowPlacement> placements, bool visible)
    {
        if (pendingActivation is not { } panel) return;
        if (panel.WorkspaceId != Session.SelectedWorkspace || !Session.Panels.Contains(panel))
        {
            pendingActivation = null;
            return;
        }
        if (!visible || dialogDepth > 0 || !placements.Any(placement => ReferenceEquals(placement.Record, panel.Record))) return;
        pendingActivation = null;
        Session.Backend.Activate(panel.Record);
    }

    private void RestoreHost()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = lastVisibleState;
        Activate();
    }

    private void ToggleHost()
    {
        var groupFocused = OperatingSystem.IsWindows() && Session.Backend is WindowsWindowBackend windows
            ? windows.GroupHasForeground() : IsActive;
        if (IsVisible && WindowState != WindowState.Minimized && groupFocused)
        {
            pendingActivation = null;
            WindowState = WindowState.Minimized;
        }
        else if (Session.ActivePanel(Session.Current) is { } panel)
            ActivatePanel(panel);
        else
            RestoreHost();
    }

    private void UpdateShortcuts()
    {
        if (OperatingSystem.IsWindows() && Session.Backend is WindowsWindowBackend windows)
            unavailableShortcuts = windows.ConfigureShortcuts(Session.Config).ToHashSet();
    }

    private async Task EditShortcutsAsync()
    {
        var dialog = new ShortcutsWindow(Session.Config, unavailableShortcuts);
        if (!await ShowEditorAsync(dialog)) return;
        Session.Config.Shortcuts = dialog.Result;
        UpdateShortcuts();
        Session.Save();
        if (unavailableShortcuts.Count > 0)
            SetStatus(L10n.Format("Workflow.ShortcutConflicts", string.Join(", ", DockShortcuts.Commands
                .Where(command => unavailableShortcuts.Contains(command.Id)).Select(DockShortcuts.Label))));
    }

    private void ShowCollectMenu() => OpenMenu(collect, [
        AsyncItem(L10n.Get("Workflow.CollectOptions"), CollectOptionsAsync),
        Item(L10n.Get("RuleDialogs.TerminalsOnly"), () => CollectWindows(new(TerminalsOnly: true))),
        AsyncItem(L10n.Get("Workflow.CaptureReport"), CaptureReportAsync, Session.LastCapture.Count > 0)
    ], PlacementMode.BottomEdgeAlignedRight);

    private void CollectWindows(CaptureOptions options, IReadOnlyList<WindowCandidate>? windows = null)
    {
        Session.Collect(windows ?? Session.Backend.Enumerate(), options);
        RefreshStatus();
    }

    private async Task CollectOptionsAsync()
    {
        var windows = Session.Backend.Enumerate();
        var dialog = new CaptureOptionsWindow(Session, windows);
        if (await ShowEditorAsync(dialog)) CollectWindows(dialog.Options, windows);
    }

    private async Task CaptureReportAsync() => await ShowEditorAsync(new CaptureReportWindow(Session.LastCapture));

    private async Task OpenProjectAsync(ProjectProfile profile)
    {
        var plan = Session.ProjectPlan(profile, Session.Backend.Enumerate());
        var dialog = new ProjectOpenWindow(profile, plan);
        if (!await ShowEditorAsync(dialog)) return;
        var result = Session.OpenProject(profile, dialog.StartMissing, dialog.Resume, dialog.RetryUnmatched);
        SetStatus(L10n.Format("Workflow.ProjectSummary",
            result.Count(role => role.State == ProjectRoleState.Open),
            result.Count(role => role.State == ProjectRoleState.Starting),
            result.Count(role => role.State is ProjectRoleState.Ambiguous or ProjectRoleState.Failed or ProjectRoleState.Missing)));
        if (result.Any(role => role.State == ProjectRoleState.Failed))
            await ShowEditorAsync(new ProjectReportWindow(result));
        if (result.Select(role => role.Panel).OfType<RuntimePanel>().MaxBy(panel => panel.ActivatedUtc) is { } panel)
            ActivatePanel(panel);
    }

    private MenuItem LinkProjectMenu(RuntimePanel panel) => Group(L10n.Get("Workflow.LinkProjectRole"),
        Session.Config.Projects.Select(profile => Group(profile.Name,
            profile.Commands.Select(command => Item(command.Name, () => Session.BindProjectRole(panel, profile, command),
                check: panel.ProjectId == profile.Id && panel.CommandId == command.Id)))));

    private async Task CreateRuleAsync(RuntimePanel panel, bool exclude)
    {
        var rule = WindowRule.Create(panel.WorkspaceId);
        rule.Name = panel.Caption;
        rule.ProcessPattern = panel.Record.ProcessName;
        rule.TitlePattern = panel.Title;
        rule.Exclude = exclude;
        if (!await ShowEditorAsync(new RuleEditor(rule, Session.Config.Workspaces))) return;
        Session.Config.Rules.Add(rule);
        Session.Save();
    }

    private void QueueAttentionUpdate()
    {
        if (closing || attentionUpdateQueued) return;
        attentionUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            attentionUpdateQueued = false;
            if (!closing) UpdateAttentionCount();
        }, DispatcherPriority.Background);
    }

    internal void SetAgentNotifications(bool enabled, bool onCompletion)
    {
        if (Session.Config.AgentNotifications == enabled && Session.Config.NotifyOnCompletion == onCompletion) return;
        Session.Config.AgentNotifications = enabled;
        Session.Config.NotifyOnCompletion = onCompletion;
        UpdateAttentionCount();
        Session.Save();
    }

    private void UpdateAttentionCount()
    {
        var waiting = Session.AgentActivities().Where(activity => activity.NeedsAttention).ToArray();
        attentionCount.Text = waiting.Length.ToString();
        attentionCount.IsVisible = waiting.Length > 0;
        attentionCount.Foreground = DockTheme.Accent;
        var label = waiting.Length == 0 ? L10n.Get("Workflow.AgentActivity") : L10n.Format("Workflow.AgentActivityCount", waiting.Length);
        ToolTip.SetTip(attention, label);
        AutomationProperties.SetName(attention, label);
        var counts = waiting.Where(activity => activity.Panel is not null).GroupBy(activity => activity.Panel!.WorkspaceId)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var (id, tab) in tabButtons)
        {
            var active = Session.Panels.Count(panel => panel.WorkspaceId == id);
            var needsAttention = counts.GetValueOrDefault(id);
            tab.Count.Text = needsAttention == 0 ? active.ToString() : $"{active} · {needsAttention}!";
            if (needsAttention > 0) tab.Count.Foreground = DockTheme.Accent;
        }
        if (nativeReady && OperatingSystem.IsWindows() && Session.Backend is WindowsWindowBackend windows)
            windows.ConfigureAgentTray(Session.Config.AgentNotifications, "WindowDock · " + label);
    }

    private void OnAgentStatusTransition(AgentActivity activity)
    {
        if (closing || !Session.Config.AgentNotifications || !nativeReady ||
            !OperatingSystem.IsWindows() || Session.Backend is not WindowsWindowBackend windows) return;
        if (activity.NeedsAttention)
            windows.NotifyAgent(L10n.Format("Workflow.AgentActivityCount", Session.AgentActivities().Count(item => item.NeedsAttention)));
        else if (Session.Config.NotifyOnCompletion)
            windows.NotifyAgent(L10n.Get("Workflow.AgentCompleted"));
    }

    private async Task OpenAttentionAsync()
    {
        if (closing || dialogDepth > 0 && ActiveMenu is null) return;
        RestoreHost();
        var dialog = new AgentAttentionWindow(Session);
        if (!await ShowEditorAsync(dialog) || dialog.Selected is not { } selected) return;
        if (selected.Panel is { } panel && Session.Panels.Contains(panel))
        {
            lastAgentKey = selected.Key;
            ActivatePanel(panel);
        }
        else if (Session.HookSessions.ContainsKey(selected.Key))
        {
            var picker = new PanelPicker(Session);
            if (!await ShowEditorAsync(picker) || picker.Selected is not { } destination) return;
            Session.BindHook(selected.Key, destination);
            ActivatePanel(destination);
        }
    }

    private void NextAgent()
    {
        var waiting = Session.AgentActivities().Where(activity => activity.NeedsAttention && activity.Panel is not null).ToArray();
        if (waiting.Length == 0)
        {
            _ = UserActionAsync(OpenAttentionAsync);
            return;
        }
        var last = Array.FindIndex(waiting, activity => activity.Key == lastAgentKey);
        var next = waiting[(last + 1) % waiting.Length];
        lastAgentKey = next.Key;
        ActivatePanel(next.Panel!);
    }

    private Border CreateStackBar()
    {
        var button = DockControls.Button("", "grid", tip: L10n.Get("Workflow.Stack"));
        button.Height = 25;
        ((StackPanel)button.Content!).Children.Add(stackLabel);
        button.Click += (_, _) => OpenMenu(button, Session.CurrentPanels.Select(panel => (Control)Item(StackCaption(panel),
            () => ActivatePanel(panel), check: Session.StackPanel() == panel)).ToArray(), PlacementMode.Bottom);
        stackBar.Padding = new Thickness(5, 1);
        stackBar.Child = button;
        return stackBar;
    }

    private static string StackCaption(RuntimePanel panel) => string.IsNullOrEmpty(panel.StatusText)
        ? panel.Caption
        : $"{panel.Caption} · {panel.StatusText}";

    private bool ShowStack(IReadOnlyList<RuntimePanel> panels, int width)
    {
        var height = Math.Max(1, (int)workspace.Bounds.Height);
        var layout = Session.Current.Layout;
        var useStack = panels.Count > 0 && Session.FocusedPanel is null &&
            (layout == "stack" || layout == "adaptive" && FlexibleLayout.Plan(panels.Count,
                new DrawingSize(width, height), Session.Config.MinWidth, Session.Config.MinHeight, Session.Current, 4).Extent.Height > height);
        stackBar.IsVisible = useStack;
        if (useStack && Session.StackPanel() is { } active)
        {
            stackBar.Background = DockTheme.Surface;
            stackLabel.Text = $"{StackCaption(active)}  ·  {panels.Count}";
        }
        return useStack;
    }

    private void ArrangeStack(IReadOnlyList<RuntimePanel> panels, int width, int height)
    {
        if (Session.StackPanel() is not { } active) return;
        var pinned = panels.FirstOrDefault(panel => panel.Id == Session.Current.PinnedPanelId && panel != active);
        var horizontal = width >= 2 * Session.Config.MinWidth + 12;
        var vertical = height >= 2 * Session.Config.MinHeight + 12;
        if (!horizontal && !vertical) pinned = null;
        foreach (var panel in panels)
            cards[panel.Id].IsVisible = panel == active || panel == pinned;

        var x = 4;
        var y = 4;
        if (pinned is not null)
        {
            if (horizontal)
            {
                var split = (width - 12) / 2;
                Place(cards[pinned.Id], new(4, 4, split, Math.Max(1, height - 8)));
                x = split + 8;
            }
            else
            {
                var split = (height - 12) / 2;
                Place(cards[pinned.Id], new(4, 4, Math.Max(1, width - 8), split));
                y = split + 8;
            }
        }
        Place(cards[active.Id], new(x, y, Math.Max(1, width - x - 4), Math.Max(1, height - y - 4)));
    }
}
