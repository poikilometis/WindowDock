using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WindowDock;

internal sealed class AgentActivityCard : Border
{
    private readonly WorkspaceSession session;
    private readonly ListBox list;
    private readonly TextBlock empty;
    private readonly CheckBox waitingOnly;
    private readonly Button close;

    internal AgentActivityCard(WorkspaceSession session, bool onlyWaiting, Action dismiss, Action<AgentActivity> activate)
    {
        this.session = session;
        Width = 360;
        Background = DockTheme.Surface;
        BorderBrush = DockTheme.Border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12, 8, 12, 12);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

        var title = new TextBlock
        {
            Text = L10n.Get("Workflow.AgentActivity"),
            FontSize = 14, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        };
        close = DockControls.Button("", "close", dismiss, tip: L10n.Get("BasicDialogs.Close"));
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        waitingOnly = new CheckBox
        {
            Content = new TextBlock { Text = L10n.Get("Workflow.WaitingOnly"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
            IsChecked = onlyWaiting, MinHeight = 28
        };
        AutomationProperties.SetName(waitingOnly, L10n.Get("Workflow.WaitingOnly"));
        waitingOnly.IsCheckedChanged += (_, _) => Refresh();
        list = new ListBox { ItemTemplate = Rows.Template<AgentActivity>(CreateRow), Classes = { "activity-list" } };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(list, L10n.Get("Workflow.AgentActivity"));
        list.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            if (args.InitialPressMouseButton == MouseButton.Left &&
                (args.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true)?.Content is AgentActivity activity)
            {
                args.Handled = true;
                activate(activity);
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        list.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && list.SelectedItem is AgentActivity activity)
            {
                args.Handled = true;
                activate(activity);
            }
        };
        empty = DockControls.Note("");
        empty.Margin = new Thickness(0, 4, 0, 2);
        var body = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 4 };
        body.Children.Add(header);
        Grid.SetRow(waitingOnly, 1);
        body.Children.Add(waitingOnly);
        Grid.SetRow(list, 2);
        body.Children.Add(list);
        Grid.SetRow(empty, 2);
        body.Children.Add(empty);
        Child = body;
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            dismiss();
        }, RoutingStrategies.Tunnel);
        Refresh();
    }

    internal bool WaitingOnly => waitingOnly.IsChecked == true;
    internal void FocusContent()
    {
        if (list.IsVisible) list.Focus();
        else close.Focus();
    }

    internal void Refresh()
    {
        var items = session.AgentActivities();
        var shown = WaitingOnly ? items.Where(activity => activity.NeedsAttention).ToArray() : items;
        var selected = (list.SelectedItem as AgentActivity)?.Key;
        list.ItemsSource = shown;
        list.SelectedItem = shown.FirstOrDefault(item => item.Key == selected) ?? shown.FirstOrDefault();
        list.IsVisible = shown.Count > 0;
        empty.Text = L10n.Get(items.Count > 0 ? "Workflow.NoWaitingAgents" : "Workflow.NoAgentActivity");
        empty.IsVisible = shown.Count == 0;
    }

    private Control CreateRow(AgentActivity activity)
    {
        var workspace = session.Config.Workspaces.FirstOrDefault(workspace => workspace.Id == activity.Panel?.WorkspaceId);
        var provider = HookProvider.Find(activity.Signal.Provider)?.Name ?? activity.Signal.Provider;
        var status = L10n.Get("RuntimePanel." + (activity.Signal.Status switch
        {
            "waiting" => "NeedsInput", "error" => "Error", "working" => "Working", _ => "Done"
        }));
        var directory = string.IsNullOrEmpty(activity.Signal.Directory) ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(activity.Signal.Directory));
        var detail = string.Join(" · ", new[] { status, provider, workspace?.Name, directory }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var line = Rows.Line(activity.Panel?.Caption ?? L10n.Get("SessionDialogs.NoPanelAssigned"), detail);
        if (activity.NeedsAttention && line is StackPanel { Children.Count: 2 } row && row.Children[1] is TextBlock context)
            context.Foreground = DockTheme.Accent;
        var label = activity.Signal.SessionKey?.Split(':').LastOrDefault();
        if (label?.Length > 6) label = label[..6];
        var elapsed = DateTime.UtcNow - (activity.NeedsAttention ? activity.StateSince : activity.LastSeen);
        var duration = TimeSpan.FromSeconds(Math.Max(0, (long)elapsed.TotalSeconds)).ToString("g");
        ToolTip.SetTip(line, string.Join(" · ", new[] { detail, label, duration, activity.Signal.Message }
            .Where(value => !string.IsNullOrWhiteSpace(value))));
        return line;
    }
}
