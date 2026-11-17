using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class WindowPicker : EditorWindow
{
    public IReadOnlyList<WindowCandidate> Selected { get; private set; } = [];
    public WindowPicker(IReadOnlyList<WindowCandidate> candidates, int capacity) : base(L10n.Get("BasicDialogs.AddOpenWindows"), 740, 520, L10n.Get("BasicDialogs.Add"))
    {
        Note(L10n.Format("BasicDialogs.FreeSlotsInThisTabUseCtrl", capacity));
        var filter = Input(""); filter.PlaceholderText = L10n.Get("BasicDialogs.WindowOrApplicationName"); Body.Children.Add(filter);
        var list = new ListBox { Height = 300, SelectionMode = SelectionMode.Multiple, ItemsSource = candidates, ItemTemplate = Rows.Template<WindowCandidate>(candidate => Rows.Line(candidate.Title, candidate.ProcessName + (candidate.IsMinimized ? L10n.Get("BasicDialogs.Minimized") : ""))) };
        Body.Children.Add(list);
        if (candidates.Count == 0) Note(L10n.Get("BasicDialogs.NoAvailableWindowsDockedAndSystemWindows"));
        filter.TextChanged += (_, _) => list.ItemsSource = candidates.Where(c => c.Display.Contains(filter.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
        Commit = () =>
        {
            Selected = list.SelectedItems?.Cast<WindowCandidate>().ToArray() ?? [];
            if (Selected.Count > capacity) throw new InvalidOperationException(L10n.Get("BasicDialogs.TheSelectionExceedsTheFreeSlotsUse"));
        };
    }
}

internal static class Rows
{
    // Avalonia clears recycled item containers with null before binding their next item.
    public static IDataTemplate Template<T>(Func<T, Control> build) where T : class =>
        new FuncDataTemplate<T>((item, _) => item is null ? null : build(item));

    public static Control Line(string title, string detail)
    {
        var row = new StackPanel { Spacing = 3 };
        row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(new TextBlock { Text = detail, Foreground = DockTheme.Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }
}

internal sealed class SearchWindow : EditorWindow
{
    public RuntimePanel? Selected { get; private set; }
    public SearchWindow(WorkspaceSession session) : base(L10n.Get("BasicDialogs.FindWindow"), 680, 460, L10n.Get("BasicDialogs.GoTo"))
    {
        var search = Input(""); search.PlaceholderText = L10n.Get("BasicDialogs.PanelApplicationOrTab");
        var list = new ListBox { Height = 270, ItemTemplate = Rows.Template<RuntimePanel>(panel => Rows.Line(panel.Caption, session.Config.Workspaces.First(w => w.Id == panel.WorkspaceId).Name + " · " + panel.Record.ProcessName)) };
        var empty = DockControls.Note(L10n.Get("BasicDialogs.NoMatches")); Body.Children.Add(search); Body.Children.Add(list); Body.Children.Add(empty);
        void Update() { var values = session.Search(search.Text ?? ""); list.ItemsSource = values; list.SelectedItem = values.FirstOrDefault(); empty.IsVisible = values.Count == 0; }
        search.TextChanged += (_, _) => Update(); Update();
        Commit = () => Selected = list.SelectedItem as RuntimePanel ?? throw new InvalidOperationException(L10n.Get("BasicDialogs.SelectAWindow"));
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is RuntimePanel panel) { Selected = panel; Finish(); } };
        Opened += (_, _) => search.Focus();
        search.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Down) { list.Focus(); args.Handled = true; }
            else if (args.Key == Avalonia.Input.Key.Enter && list.SelectedItem is RuntimePanel panel) { Selected = panel; Finish(); args.Handled = true; }
        };
    }
    private void Finish() { Commit?.Invoke(); MarkAccepted(); }
}

internal sealed class SettingsWindow : EditorWindow
{
    private readonly AppConfig config;
    private readonly WorkspaceConfig workspace;
    private readonly NumericUpDown minimumWidth, minimumHeight, capacity;
    public SettingsWindow(AppConfig config, WorkspaceConfig workspace) : base(L10n.Get("BasicDialogs.WindowSizesAndLimit"), 600, 350)
    {
        this.config = config; this.workspace = workspace;
        minimumWidth = Number(config.MinWidth, 320, 1200); minimumHeight = Number(config.MinHeight, 200, 900); capacity = Number(workspace.Capacity, 1, 25);
        Field(L10n.Get("BasicDialogs.MinimumWidth"), minimumWidth); Field(L10n.Get("BasicDialogs.MinimumHeight"), minimumHeight); Field(L10n.Get("BasicDialogs.WindowsPerTab"), capacity);
        Note(L10n.Get("BasicDialogs.TheGridFitsTheAvailableWidthA"));
        Commit = () => { if ((int)(capacity.Value ?? 1) < 1) throw new InvalidOperationException(L10n.Get("BasicDialogs.TheLimitMustBeBetweenAnd")); };
    }
    public void Apply() { config.MinWidth = (int)(minimumWidth.Value ?? 440); config.MinHeight = (int)(minimumHeight.Value ?? 280); workspace.Capacity = (int)(capacity.Value ?? 15); }
}

internal sealed class HelpWindow : EditorWindow
{
    public HelpWindow() : base("WindowDock", 620, 490, L10n.Get("BasicDialogs.Close"))
    {
        Note("WindowDock " + RuntimePaths.ProductVersion);
        Note(L10n.Get("BasicDialogs.CompactWorkspacesForExistingWindowsNETLTS"));
        foreach (var text in new[] {
            L10n.Get("BasicDialogs.AddWindowsSelectsExistingApplicationsCollectWindows"),
            L10n.Get("BasicDialogs.DoubleClickAPanelHeaderToZoom"),
            L10n.Get("BasicDialogs.CtrlShiftClickHeadersToSelectPanels"),
            L10n.Get("BasicDialogs.CtrlAltTabsCtrlAltEnterZoom"),
            L10n.Get("BasicDialogs.CtrlAltDMinimizeTheGroupCtrl"),
            L10n.Get("BasicDialogs.RulesProjectsGroupRestoreAndAgentIntegrations")
        }) Body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    }
}