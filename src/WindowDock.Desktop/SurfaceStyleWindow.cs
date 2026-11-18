using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal enum SurfaceArea { Window, Header, Tab }

internal sealed class SurfaceStyleWindow : EditorWindow
{
    private readonly MainWindow host;
    private readonly SurfaceStyle? defaults;
    private readonly ComboBox fill;
    private readonly Slider opacity;
    private readonly CheckBox? ownOpacity, ownTitle;
    private readonly Border sample = new() { Height = 72, CornerRadius = new(8), Padding = new(14) };
    private readonly TextBlock sampleTitle;
    private readonly SurfaceBrushes preview = new();
    private readonly Button startButton, endButton, titleButton;
    private uint start, end, titleColor;

    internal SurfaceStyle? Result { get; private set; }

    internal SurfaceStyleWindow(MainWindow host, string caption, SurfaceArea area, SurfaceStyle? current,
        SurfaceStyle? defaults = null, bool scoped = false, bool nativeOpacity = true)
        : base(L10n.Get("Surface.Colors"), 520, 455)
    {
        this.host = host;
        this.defaults = defaults;
        var effective = SurfaceStyle.Resolve(defaults, current);
        start = effective.StartColor ?? ((ISolidColorBrush)DockTheme.Surface).Color.ToUInt32();
        end = effective.EndColor ?? ((ISolidColorBrush)DockTheme.Accent).Color.ToUInt32();
        titleColor = effective.TitleColor ?? ((ISolidColorBrush)DockTheme.Text).Color.ToUInt32();
        sampleTitle = new() { Text = caption, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        sample.Child = sampleTitle;
        Body.Children.Add(sample);

        fill = Choices(new Choice<string?>[]
        {
            new(null, L10n.Get(scoped ? "Surface.Inherit" : "Surface.FromTheme")),
            new("theme", L10n.Get("Surface.FromTheme")),
            new("solid", L10n.Get("BackgroundWindow.Color")),
            new("gradient", L10n.Get("Surface.StaticGradient")),
            new("flow", L10n.Get("Surface.FlowGradient"))
        }.Where(item => scoped || item.Value != "theme"), value => value == current?.Fill);
        Field(L10n.Get("BackgroundWindow.BackgroundType"), fill);
        startButton = Swatch();
        endButton = Swatch();
        titleButton = Swatch();
        var colors = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { startButton, endButton } };
        Body.Children.Add(colors);

        var minimum = area == SurfaceArea.Window ? 15 : 0;
        opacity = new() { Minimum = minimum, Maximum = 100, Value = effective.OpacityPercent(minimum), TickFrequency = 1, IsSnapToTickEnabled = true };
        if (area != SurfaceArea.Tab && nativeOpacity)
        {
            var percent = new TextBlock { Text = opacity.Value.ToString("0") + "%", VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new("*,46"), ColumnSpacing = 8, Children = { opacity } };
            Grid.SetColumn(percent, 1);
            row.Children.Add(percent);
            if (scoped)
            {
                ownOpacity = Check(L10n.Get("Surface.CustomOpacity"), current?.Opacity is not null);
                ownOpacity.IsCheckedChanged += (_, _) => Update();
                Body.Children.Add(ownOpacity);
            }
            Field(L10n.Get("ColorPickerView.Opacity"), row);
            opacity.PropertyChanged += (_, args) =>
            {
                if (args.Property != Slider.ValueProperty) return;
                percent.Text = opacity.Value.ToString("0") + "%";
                Update();
            };
        }
        else if (area == SurfaceArea.Window && !nativeOpacity) Note(L10n.Get("Surface.NativeOpacityUnsupported"));
        if (area == SurfaceArea.Window)
        {
            ownTitle = Check(L10n.Get("Surface.TitleColor"), current?.TitleColor is not null);
            ownTitle.IsCheckedChanged += (_, _) => Update();
            Body.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { ownTitle, titleButton } });
        }
        var reset = DockControls.Button(L10n.Get("Surface.Reset"), "back", () =>
        {
            fill.SelectedIndex = 0;
            if (ownOpacity is not null) ownOpacity.IsChecked = false;
            if (ownTitle is not null) ownTitle.IsChecked = false;
            opacity.Value = defaults?.OpacityPercent(minimum) ?? 100;
            Update();
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(reset);
        fill.SelectionChanged += (_, _) => Update();
        startButton.Click += (_, _) => _ = ChooseColorAsync(start, selected => start = selected);
        endButton.Click += (_, _) => _ = ChooseColorAsync(end, selected => end = selected);
        titleButton.Click += (_, _) => _ = ChooseColorAsync(titleColor, selected => titleColor = selected);
        Opened += (_, _) => preview.SetRunning(true);
        Closed += (_, _) => preview.Dispose();
        Commit = () => Result = ReadStyle();
        Update();

        SurfaceStyle? ReadStyle()
        {
            var mode = fill.SelectedItem is Choice<string?> choice ? choice.Value : null;
            int? alpha = area != SurfaceArea.Tab && nativeOpacity && (ownOpacity is null || ownOpacity.IsChecked == true)
                ? (int)opacity.Value : null;
            uint? ink = ownTitle?.IsChecked == true ? titleColor : null;
            if (mode is null && alpha is null && ink is null) return null;
            return new() { Fill = mode, StartColor = start, EndColor = end, Opacity = alpha, TitleColor = ink };
        }

        void Update()
        {
            var working = ReadStyle();
            var mode = working?.Fill;
            startButton.IsVisible = mode is "solid" or "gradient" or "flow";
            endButton.IsVisible = mode is "gradient" or "flow";
            titleButton.IsVisible = ownTitle?.IsChecked == true;
            opacity.IsEnabled = ownOpacity is null || ownOpacity.IsChecked == true;
            PaintSwatch(startButton, start, L10n.Get("Surface.StartColor"));
            PaintSwatch(endButton, end, L10n.Get("Surface.EndColor"));
            PaintSwatch(titleButton, titleColor, L10n.Get("Surface.TitleColor"), showLabel: false);
            var resolved = SurfaceStyle.Resolve(defaults, working);
            preview.Reset();
            sample.Background = preview.Get(resolved, DockTheme.Surface, resolved.OpacityPercent());
            sampleTitle.Foreground = resolved.TitleColor is { } ink ? new SolidColorBrush(Color.FromUInt32(ink)) : DockTheme.Text;
        }

        async Task ChooseColorAsync(uint before, Action<uint> apply)
        {
            var editor = new SurfaceColorWindow(Color.FromUInt32(before));
            if (!await host.ShowEditorAsync(editor, this)) return;
            apply(editor.Result.ToUInt32());
            Update();
        }
    }

    private static Button Swatch()
    {
        var button = DockControls.Button("", "more");
        button.Height = 30;
        return button;
    }

    private static void PaintSwatch(Button button, uint value, string label, bool showLabel = true)
    {
        var color = Color.FromUInt32(value);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new Border { Width = 13, Height = 13, CornerRadius = new(3), Background = new SolidColorBrush(color) });
        if (showLabel) content.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        content.Children.Add(new TextBlock { Text = color.A == 255 ? $"#{value & 0xFFFFFF:X6}" : $"#{value:X8}", FontSize = 11, Foreground = DockTheme.Muted });
        button.Content = content;
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
    }
}

internal sealed class SurfaceColorWindow : EditorWindow
{
    internal Color Result { get; private set; }

    internal SurfaceColorWindow(Color initial) : base(L10n.Get("BackgroundWindow.Color"), 530, 410)
    {
        var picker = new ColorPickerView(initial);
        Body.Children.Add(picker);
        Commit = () => Result = picker.GetColor();
    }
}
