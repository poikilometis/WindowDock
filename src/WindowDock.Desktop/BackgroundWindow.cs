using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class BackgroundWindow : EditorWindow
{
    private readonly WorkspaceConfig workspace;
    private readonly ComboBox mode;
    private readonly TextBox path;
    internal ColorPickerView Picker { get; }
    public BackgroundWindow(WorkspaceConfig workspace) : base(L10n.Get("BackgroundWindow.TabBackground"), 680, 500, L10n.Get("BackgroundWindow.Save"))
    {
        this.workspace = workspace;
        mode = Choices(new[] { new Choice<string>("solid", L10n.Get("BackgroundWindow.Color")), new("image", L10n.Get("BackgroundWindow.ImageGIF")), new("transparent", L10n.Get("BackgroundWindow.Transparent")) }, id => workspace.BackgroundMode == id);
        Picker = new(Color.FromUInt32(unchecked((uint)workspace.BackgroundColor)));
        path = Input(workspace.BackgroundPath ?? ""); Field(L10n.Get("BackgroundWindow.BackgroundType"), mode); Body.Children.Add(Picker);
        var browse = DockControls.Button(L10n.Get("BackgroundWindow.Browse"), "", () => _ = BrowseAsync()); browse.Classes.Add("secondary");
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 }; row.Children.Add(path); Grid.SetColumn(browse, 1); row.Children.Add(browse);
        var file = new StackPanel { Spacing = 8 }; file.Children.Add(DockControls.Note(L10n.Get("BackgroundWindow.ImageOrGIFFile"))); file.Children.Add(row);
        file.Children.Add(DockControls.Note(L10n.Get("BackgroundWindow.ImagesAndAnimatedGIFsUpToMB"))); Body.Children.Add(file);
        var transparent = DockControls.Note(L10n.Get("BackgroundWindow.TheDesktopShowsThroughEmptyTabSpace")); Body.Children.Add(transparent);
        void Toggle() { var selected = Value<string>(mode); Picker.IsVisible = selected == "solid"; file.IsVisible = selected == "image"; transparent.IsVisible = selected == "transparent"; Height = selected == "solid" ? 500 : 340; }
        mode.SelectionChanged += (_, _) => Toggle(); Toggle();
        Commit = () =>
        {
            if (Value<string>(mode) == "solid") Picker.GetColor();
            if (Value<string>(mode) == "image" && !File.Exists(path.Text)) throw new InvalidOperationException(L10n.Get("BackgroundWindow.TheSelectedFileWasNotFound"));
        };
    }
    private async Task BrowseAsync()
    {
        try
        {
            var files = await PresentationHost.StorageProvider.OpenFilePickerAsync(new() { Title = L10n.Get("BackgroundWindow.TabBackground"), AllowMultiple = false, FileTypeFilter = [new(L10n.Get("BackgroundWindow.ImagesAndGIFs")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] }] });
            if (files.FirstOrDefault() is { } file) path.Text = file.Path.LocalPath;
        }
        catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
    }
    public void Apply()
    {
        workspace.BackgroundMode = Value<string>(mode); workspace.BackgroundPath = path.Text?.Trim() ?? "";
        if (workspace.BackgroundMode == "solid") workspace.BackgroundColor = unchecked((int)Picker.GetColor().ToUInt32());
    }
}