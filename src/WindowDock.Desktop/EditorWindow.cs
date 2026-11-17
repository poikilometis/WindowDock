using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace WindowDock;

internal class EditorWindow : Window
{
    protected StackPanel Body { get; } = new() { Spacing = 12 };
    protected TextBlock Error { get; } = new() { IsVisible = false, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    public bool Accepted { get; private set; }
    public Action? Commit { get; set; }
    private EditorWindow? presentationHost;
    private EditorWindow currentPage;
    private TaskCompletionSource<bool>? completion;
    private readonly Stack<(EditorWindow Page, Control Content, string Title, Avalonia.Input.IInputElement? Focus)> navigation = [];
    private readonly Button back;
    internal EditorWindow PresentationHost => presentationHost ?? this;
    protected void MarkAccepted() => FinishPage(true);
    internal void FinishPage(bool accepted)
    {
        Accepted = accepted;
        if (completion is null) { Close(accepted); return; }
        var host = PresentationHost;
        if (host.currentPage != this) return;
        var result = completion; completion = null;
        var pageContent = (Control)host.Content!; host.Content = null; Content = pageContent;
        var previous = host.navigation.Pop(); host.Content = previous.Content; host.Title = previous.Title; host.currentPage = previous.Page;
        presentationHost = null; back.IsVisible = false;
        host.FocusPage(previous.Focus); Close(); result.TrySetResult(accepted);
    }
    internal Task<bool> ShowNestedAsync(EditorWindow page)
    {
        var host = PresentationHost;
        if (!host.IsVisible || host.currentPage != this) throw new InvalidOperationException(L10n.Get("EditorWindow.ThisSettingsPageIsAlreadyClosed"));
        var previous = (Control)host.Content!; var next = (Control)page.Content!;
        page.Content = null; host.Content = null;
        host.navigation.Push((this, previous, host.Title ?? "WindowDock", host.FocusManager?.GetFocusedElement()));
        page.presentationHost = host; page.back.IsVisible = true;
        page.completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.currentPage = page; host.Content = next; host.Title = page.Title;
        host.FocusPage(); return page.completion.Task;
    }
    private void FocusPage(Avalonia.Input.IInputElement? preferred = null)
    {
        var page = currentPage;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (currentPage != page || !IsVisible) return;
            if (preferred is Avalonia.Input.InputElement input && input.IsEffectivelyVisible && input.IsEffectivelyEnabled && input.Focus()) return;
            var first = page.Body.GetVisualDescendants().OfType<Avalonia.Input.InputElement>().FirstOrDefault(control => control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
            first?.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
    public EditorWindow(string title, double width = 650, double height = 480, string? accept = null)
    {
        currentPage = this;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Title = title; Width = width; Height = height; MinWidth = Math.Min(480, width); MinHeight = Math.Min(280, height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; CanResize = true;
        Classes.Add("editor");
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(22) };
        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Medium, Margin = new(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap };
        var header = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        back = DockControls.Button(L10n.Get("EditorWindow.Back"), "back", () => FinishPage(false), tip: L10n.Get("EditorWindow.BackDiscardChangesOnThisPage"));
        back.IsVisible = false; back.VerticalAlignment = VerticalAlignment.Top; back.Classes.Add("secondary");
        header.Children.Add(back); Grid.SetColumn(heading, 1); header.Children.Add(heading); root.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 16, 0, 0) }; footer.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var cancel = DockControls.Button(L10n.Get("EditorWindow.Cancel"), "", () => FinishPage(false)); cancel.IsCancel = true; cancel.Classes.Add("secondary"); cancel.MinWidth = 94; cancel.Height = 30;
        var save = DockControls.Button(accept ?? L10n.Get("BackgroundWindow.Save"), "", () =>
        {
            try { Commit?.Invoke(); FinishPage(true); }
            catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
        }, true); save.IsDefault = true; save.MinWidth = 112; save.Height = 30;
        buttons.Children.Add(cancel); buttons.Children.Add(save); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        AddHandler(KeyDownEvent, (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) { currentPage.FinishPage(false); args.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            while (navigation.Count > 0)
            {
                var page = currentPage; var previous = navigation.Pop(); currentPage = previous.Page;
                page.Accepted = false; page.presentationHost = null; var result = page.completion; page.completion = null;
                page.Close(); result?.TrySetResult(false);
            }
        };
    }
    protected void Field(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new("170,*"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(control, 1); grid.Children.Add(control); Body.Children.Add(grid);
        Avalonia.Automation.AutomationProperties.SetName(control, label);
    }
    protected static TextBox Input(string text, bool multiline = false) => new()
    { Text = text, AcceptsReturn = multiline, MinHeight = multiline ? 80 : 30, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
    protected static NumericUpDown Number(int value, int minimum, int maximum) => new() { Value = value, Minimum = minimum, Maximum = maximum, Increment = 1, FormatString = "0" };
    protected static CheckBox Check(string text, bool value)
    {
        var check = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        Avalonia.Automation.AutomationProperties.SetName(check, text); return check;
    }
    protected static ComboBox Choices<T>(IEnumerable<Choice<T>> items, Func<T, bool> selected)
    {
        var values = items.ToArray(); return new() { ItemsSource = values, SelectedItem = values.FirstOrDefault(c => selected(c.Value)), HorizontalAlignment = HorizontalAlignment.Stretch };
    }
    protected static T Value<T>(ComboBox combo) => ((Choice<T>)combo.SelectedItem!).Value;
    protected static string Required(TextBox input, string label)
    {
        var value = input.Text?.Trim(); if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(L10n.Format("EditorWindow.EnterAValueFor", label)); return value;
    }
    protected Control FolderInput(TextBox input)
    {
        var browse = DockControls.Button(L10n.Get("BackgroundWindow.Browse"), "", () => _ = BrowseFolderAsync(input)); browse.Classes.Add("secondary");
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 }; row.Children.Add(input); Grid.SetColumn(browse, 1); row.Children.Add(browse); return row;
    }
    private async Task BrowseFolderAsync(TextBox input)
    {
        try
        {
            var folders = await PresentationHost.StorageProvider.OpenFolderPickerAsync(new() { Title = L10n.Get("EditorWindow.ProjectFolder"), AllowMultiple = false });
            if (folders.FirstOrDefault() is { } folder) input.Text = folder.Path.LocalPath;
        }
        catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
    }
    protected void Note(string text) => Body.Children.Add(DockControls.Note(text));
}

internal sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal sealed class NameEditor : EditorWindow
{
    public string Result { get; private set; } = "";
    public NameEditor(string title, string initial) : base(title, 460, 250)
    {
        var input = Input(initial); input.MaxLength = 48; Body.Children.Add(input);
        Commit = () => Result = Required(input, L10n.Get("EditorWindow.Name"));
        Opened += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}