using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed partial class MainWindow
{
    internal async Task<bool> ShowEditorAsync(EditorWindow editor, Window? owner = null)
    {
        if (owner is EditorWindow page) return await page.ShowNestedAsync(editor);
        dialogDepth++; using var suspension = Session.Backend.Suspend();
        void Raise(object? sender, EventArgs args) { if (editor.TryGetPlatformHandle() is { } handle) Session.Backend.RaiseOverlay(handle.Handle); }
        editor.Opened += Raise;
        try { await editor.ShowDialog<bool>(owner ?? this); return editor.Accepted; }
        finally { editor.Opened -= Raise; dialogDepth--; QueueSync(); }
    }
    private async Task<string?> AskNameAsync(string title, string value)
    {
        var dialog = new NameEditor(title, value); return await ShowEditorAsync(dialog) ? dialog.Result : null;
    }
    private async Task NewWorkspaceAsync()
    {
        var name = await AskNameAsync(L10n.Get("WorkspaceSession.NewTab"), Session.NextWorkspaceName());
        if (name is not null) Session.SelectedWorkspace = Session.NewWorkspace(name).Id;
    }
    private async Task RenameWorkspaceAsync(WorkspaceConfig workspace)
    {
        var name = await AskNameAsync(L10n.Get("MainWindowCommands.RenameTab"), workspace.Name); if (name is not null) Session.RenameWorkspace(workspace, name);
    }
    private async Task AddWindowsAsync() => await UserActionAsync(async () =>
    {
        var dialog = new WindowPicker(Session.Backend.Enumerate(), Math.Max(0, Session.Current.Capacity - Session.CurrentPanels.Count()));
        if (!await ShowEditorAsync(dialog)) return;
        foreach (var candidate in dialog.Selected) UserAction(() => Session.Attach(candidate));
    });
    private async Task SearchAsync()
    {
        var dialog = new SearchWindow(Session); if (!await ShowEditorAsync(dialog) || dialog.Selected is not { } panel) return;
        ActivatePanel(panel);
    }
    private MenuItem Item(string title, Action action, bool enabled = true, bool? check = null)
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled };
        if (check.HasValue) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = check.Value; }
        item.Click += (_, _) => UserAction(action); return item;
    }
    private MenuItem AsyncItem(string title, Func<Task> action, bool enabled = true) => Item(title, () => _ = UserActionAsync(action), enabled);
    private static MenuItem Group(string title, IEnumerable<Control> children) => new() { Header = title, ItemsSource = children.ToArray() };
    internal ContextMenu? ActiveMenu { get; private set; }
    internal void OpenAppMenuForTest() => ShowAppMenu();
    private void OpenMenu(Control anchor, IEnumerable<Control> entries, PlacementMode placement = PlacementMode.Pointer)
    {
        var menu = new ContextMenu { ItemsSource = entries.ToArray(), Placement = placement }; ActiveMenu = menu;
        menu.Opened += (_, _) => { dialogDepth++; if (TopLevel.GetTopLevel(menu)?.TryGetPlatformHandle() is { } popup) Session.Backend.RaiseOverlay(popup.Handle); };
        menu.Closed += (_, _) => { if (ActiveMenu == menu) ActiveMenu = null; dialogDepth = Math.Max(0, dialogDepth - 1); QueueSync(); };
        menu.Open(anchor);
    }
    private void ShowAppMenu()
    {
        List<Control> entries =
        [
            AsyncItem(L10n.Get("MainWindowCommands.FindWindow"), SearchAsync),
            Item(L10n.Get("Workflow.PreviousWindow"), () =>
            {
                if (Session.PreviousPanel() is { } previous) ActivatePanel(previous);
            }, Session.PreviousPanel() is not null),
            AsyncItem(L10n.Get("Workflow.AgentActivity"), OpenAttentionAsync),
            AsyncItem(L10n.Get("Workflow.CollectOptions"), CollectOptionsAsync, Session.Backend.Available),
            AsyncItem(L10n.Get("Workflow.CaptureReport"), CaptureReportAsync, Session.LastCapture.Count > 0),
            LayoutMenu(Session.Current)
        ];
        entries.Add(Group(L10n.Get("MainWindowCommands.Workspaces"), [
            Item(L10n.Get("MainWindowCommands.SaveGroups"), () => Session.Save()),
            Item(L10n.Get("MainWindowCommands.RestoreAvailableWindows"), () => Session.Poll(true)),
            AsyncItem(L10n.Format("MainWindowCommands.PendingWindows", Session.Pending.Count), async () => { await ShowEditorAsync(new PendingGroupsWindow(this)); }),
            Item(L10n.Get("MainWindowCommands.RestoreOnStartup"), () => { Session.Config.RestoreGroups = !Session.Config.RestoreGroups; Session.Save(); }, check: Session.Config.RestoreGroups),
            new Separator(), AsyncItem(L10n.Get("MainWindowCommands.NewTab"), NewWorkspaceAsync), AsyncItem(L10n.Get("MainWindowCommands.RenameCurrentTab"), () => RenameWorkspaceAsync(Session.Current)),
            Item(L10n.Get("MainWindowCommands.CloseCurrentTab"), () => Session.CloseWorkspace(Session.Current))
        ]));
        entries.Add(AsyncItem(L10n.Get("MainWindowCommands.CaptureRules"), async () => { var dialog = new RulesWindow(this); if (await ShowEditorAsync(dialog)) { Session.Config.Rules = dialog.Result; Session.Config.AutomaticRules = dialog.Automatic; Session.Save(); } }));
        entries.Add(AsyncItem(L10n.Get("MainWindowCommands.AgentIntegrations"), async () => { await ShowEditorAsync(new IntegrationWindow(this)); }));
        List<Control> projects = Session.Config.Projects.Select(profile => (Control)AsyncItem(profile.Name, () => OpenProjectAsync(profile))).ToList();
        if (projects.Count > 0) projects.Add(new Separator());
        projects.Add(AsyncItem(L10n.Get("MainWindowCommands.ManageProjects"), async () => { var dialog = new ProjectsWindow(this); if (await ShowEditorAsync(dialog)) { Session.Config.Projects = dialog.Result; Session.Save(); } }));
        entries.Add(Group(L10n.Get("MainWindowCommands.Projects"), projects));
        var selected = Session.ActionPanels();
        entries.Add(Group(L10n.Format("MainWindowCommands.Selection", selected.Count), [Item(L10n.Get("MainWindowCommands.SelectAllPanels"), () => Session.SelectAll(true)), Item(L10n.Get("MainWindowCommands.ClearSelection"), () => Session.SelectAll(false)), MoveMenu(selected), Item(L10n.Get("MainWindowCommands.UndockSelected"), () => Session.ReleasePanels(selected), selected.Count > 0)]));
        entries.Add(Item(Session.CanUndo ? L10n.Format("MainWindowCommands.Undo", Session.UndoName) : L10n.Get("MainWindowCommands.UndoAction"), Session.Undo, Session.CanUndo)); entries.Add(new Separator());
        entries.Add(Group(L10n.Get("MainWindowCommands.View"), [
            AsyncItem(L10n.Get("MainWindowCommands.Appearance"), ChangeAppearanceAsync),
            AsyncItem(L10n.Get("MainWindowCommands.TabBackground"), () => BackgroundAsync(Session.Current)),
            Item(L10n.Get("MainWindowCommands.ScrollOnHover"), () => { Session.Config.HoverScroll = !Session.Config.HoverScroll; Session.Backend.HoverEnabled = Session.Config.HoverScroll; Session.Save(); }, check: Session.Config.HoverScroll),
            DisplayMenu(),
            AsyncItem(L10n.Get("Workflow.Shortcuts"), EditShortcutsAsync, OperatingSystem.IsWindows()),
            Item(L10n.Get("Workflow.AgentNotifications"),
                () => SetAgentNotifications(!Session.Config.AgentNotifications, Session.Config.NotifyOnCompletion),
                OperatingSystem.IsWindows(), Session.Config.AgentNotifications),
            Item(L10n.Get("Workflow.NotifyOnCompletion"),
                () => SetAgentNotifications(Session.Config.AgentNotifications, !Session.Config.NotifyOnCompletion),
                Session.Config.AgentNotifications, Session.Config.NotifyOnCompletion)
        ]));
        entries.Add(Item(L10n.Get("MainWindowCommands.UndockTabWindows"), () => Session.ReleasePanels(Session.CurrentPanels)));
        entries.Add(Item(L10n.Get("Workflow.ReturnAllWindows"), () =>
        {
            Session.Config.Magnet = false;
            Session.Config.AutomaticRules = false;
            Session.ReleasePanels(Session.Panels.ToArray());
            Refresh();
            Session.Save();
        }, Session.Panels.Count > 0));
        entries.Add(Item(L10n.Get("MainWindowCommands.MinimizeGroup"), () => WindowState = WindowState.Minimized));
        entries.Add(AsyncItem(L10n.Get("MainWindowCommands.Help"), async () => { await ShowEditorAsync(new HelpWindow()); }));
        OpenMenu(toolbar, entries, PlacementMode.BottomEdgeAlignedRight);
    }
    private MenuItem LayoutMenu(WorkspaceConfig workspace)
    {
        List<Control> choices = [];
        foreach (var (id, name) in new[]
        {
            ("grid", L10n.Get("MainWindowCommands.Grid")), ("master", L10n.Get("MainWindowCommands.MainWindowOnTheLeft")),
            ("dual", L10n.Get("MainWindowCommands.TwoMainWindowsOnTop")),
            ("stack", L10n.Get("Workflow.Stack")), ("adaptive", L10n.Get("Workflow.Adaptive"))
        })
            choices.Add(Item(name, () => { Session.BeginLayoutChange(); workspace.Layout = id; Session.LayoutChanged(); }, check: workspace.Layout == id));
        List<Control> columns = [];
        for (var index = 0; index <= 6; index++)
        {
            var value = index; columns.Add(Item(index == 0 ? L10n.Get("MainWindowCommands.Automatic") : L10n.Columns(index), () => { Session.BeginLayoutChange(); workspace.Columns = value; workspace.ColumnWeights.Clear(); Session.LayoutChanged(); }, check: workspace.Columns == value));
        }
        choices.Add(Group(L10n.Get("MainWindowCommands.GridColumns"), columns));
        choices.Add(Item(L10n.Get("MainWindowCommands.ResetProportions"), () => { Session.BeginLayoutChange(); workspace.ColumnWeights.Clear(); workspace.RowWeights.Clear(); workspace.MasterRatio = .6; workspace.PrimaryRatio = .58; workspace.DualRatio = .5; Session.LayoutChanged(); }));
        choices.Add(AsyncItem(L10n.Get("MainWindowCommands.SizesAndLimit"), () => SettingsAsync(workspace))); return Group(L10n.Get("MainWindowCommands.Layout"), choices);
    }
    private MenuItem MoveMenu(IReadOnlyList<RuntimePanel> panels)
    {
        List<Control> destinations = [];
        foreach (var workspace in Session.Config.Workspaces)
        {
            var count = Session.Panels.Count(p => p.WorkspaceId == workspace.Id); var incoming = panels.Count(p => p.WorkspaceId != workspace.Id);
            destinations.Add(Item($"{workspace.Name} ({count}/{workspace.Capacity})", () => Session.Move(panels, workspace), incoming > 0 && count + incoming <= workspace.Capacity));
        }
        destinations.Add(new Separator()); destinations.Add(AsyncItem(L10n.Get("MainWindowCommands.NewTab"), async () =>
        {
            var name = await AskNameAsync(L10n.Get("MainWindowCommands.MoveToNewTab"), Session.NextWorkspaceName());
            if (name is null || panels.Count == 0) return;
            Session.MoveToNew(panels, name);
        }, panels.Count > 0));
        return Group(L10n.Get("MainWindowCommands.MoveToTab"), destinations);
    }
    internal void ShowPanelMenu(PanelCard card)
    {
        var panel = card.Panel; var selected = Session.ActionPanels(panel);
        OpenMenu(card, [
            Item(Session.FocusedPanel == panel.Id ? L10n.Get("MainWindowCommands.ReturnToGrid") : L10n.Get("MainWindowCommands.Zoom"), () => ToggleFocus(panel)),
            AsyncItem(L10n.Get("MainWindowCommands.Rename"), async () => { var name = await AskNameAsync(L10n.Get("WorkspaceSession.PanelName"), panel.Caption); if (name is not null) Session.RenamePanel(panel, name); }),
            MoveMenu(selected), Item(L10n.Get("MainWindowCommands.Undock"), () => Session.ReleasePanels(selected)), new Separator(),
            Item(L10n.Get("MainWindowCommands.SelectPanel"), () => Session.Select(panel, true, false)), Item(L10n.Get("MainWindowCommands.ClearAttention"), () => { panel.Attention = false; RefreshTabs(); Session.Save(); }),
            Item(L10n.Get("Workflow.PinPanel"), () => Session.TogglePinnedPanel(panel), check: Session.Current.PinnedPanelId == panel.Id),
            LinkProjectMenu(panel),
            AsyncItem(L10n.Get("Workflow.RuleFromWindow"), () => CreateRuleAsync(panel, false)),
            AsyncItem(L10n.Get("Workflow.ExcludeWindow"), () => CreateRuleAsync(panel, true)),
            AsyncItem(L10n.Get("MainWindowCommands.SetUpHooks"), async () => { await ShowEditorAsync(new IntegrationWindow(this, panel)); })
        ]);
    }
    private void ShowTabMenu(WorkspaceConfig workspace, Control anchor) => OpenMenu(anchor, [
        AsyncItem(L10n.Get("MainWindowCommands.Rename"), () => RenameWorkspaceAsync(workspace)), LayoutMenu(workspace), AsyncItem(L10n.Get("MainWindowCommands.TabBackground"), () => BackgroundAsync(workspace)),
        Item(L10n.Get("MainWindowCommands.UndockTabWindows"), () => Session.ReleasePanels(Session.Panels.Where(p => p.WorkspaceId == workspace.Id))), Item(L10n.Get("MainWindowCommands.CloseTab"), () => Session.CloseWorkspace(workspace))
    ]);
    private async Task SettingsAsync(WorkspaceConfig workspace)
    {
        var dialog = new SettingsWindow(Session.Config, workspace);
        if (await ShowEditorAsync(dialog)) { Session.BeginLayoutChange(); dialog.Apply(); Session.LayoutChanged(); }
    }
    private async Task BackgroundAsync(WorkspaceConfig workspace)
    {
        var dialog = new BackgroundWindow(workspace);
        if (await ShowEditorAsync(dialog)) { dialog.Apply(); Session.Save(); Refresh(); }
    }
}
