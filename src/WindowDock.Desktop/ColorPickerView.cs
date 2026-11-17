using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class ColorPickerView : StackPanel
{
    private readonly ColorSpectrum spectrum = new() { Height = 174 };
    private readonly ColorAxis hue = new() { Maximum = 360, SmallChange = 1 };
    private readonly ColorAxis alpha = new() { Maximum = 100, SmallChange = 1, Checkerboard = true };
    private readonly TextBox hex = new() { MinHeight = 30 };
    private readonly ComboBox format = new() { ItemsSource = new[] { "RGB", "HSV" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown[] channels = Enumerable.Range(0, 3).Select(_ => new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", MinWidth = 0, ShowButtonSpinner = false }).ToArray();
    private readonly TextBlock[] names = [new(), new(), new()];
    private readonly ColorSwatch swatch;
    private readonly TextBlock problem = new() { IsVisible = false, FontSize = 11.5, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private bool updating;
    private string? synchronizedHex;
    public Color SelectedColor { get; private set; }
    public event Action<Color>? ColorChanged;
    internal TextBox HexInput => hex;
    internal ColorSpectrum Spectrum => spectrum;
    public ColorPickerView(Color initial)
    {
        Spacing = 10; swatch = new(initial) { Height = 30 };
        var top = new Grid { ColumnDefinitions = new("*,210"), ColumnSpacing = 16 };
        top.Children.Add(spectrum);
        var values = new StackPanel { Spacing = 8 };
        values.Children.Add(swatch);
        var label = DockControls.Note("HEX"); label.FontSize = 11; values.Children.Add(label); values.Children.Add(hex); values.Children.Add(format);
        var numbers = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 7 };
        for (var i = 0; i < channels.Length; i++)
        {
            var field = new StackPanel { Spacing = 3 }; names[i].FontSize = 11; names[i].Classes.Add("note"); field.Children.Add(names[i]); field.Children.Add(channels[i]); Grid.SetColumn(field, i); numbers.Children.Add(field);
            channels[i].ValueChanged += (_, _) => FromChannels();
        }
        values.Children.Add(numbers); Grid.SetColumn(values, 1); top.Children.Add(values); Children.Add(top);
        Children.Add(AxisRow(L10n.Get("ColorPickerView.Hue"), hue)); Children.Add(AxisRow(L10n.Get("ColorPickerView.Opacity"), alpha));
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 3) };
        foreach (var value in new[] { "#13161B", "#272A30", "#4B505B", "#F3F1EB", "#FFFFFF", "#678B79", "#588A9A", "#5C78B6", "#8975B6", "#B97D86", "#BE9067", "#B5A765" })
        {
            var color = Color.Parse(value); var button = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = new SolidColorBrush(color), BorderBrush = DockTheme.Border, BorderThickness = new(1), CornerRadius = new(14) };
            AutomationProperties.SetName(button, L10n.Format("ColorPickerView.Color", value)); ToolTip.SetTip(button, value); button.Click += (_, _) => SetColor(color); presets.Children.Add(button);
        }
        Children.Add(presets); Children.Add(problem);
        AutomationProperties.SetName(hex, L10n.Get("ColorPickerView.HEXColor")); AutomationProperties.SetName(format, L10n.Get("ColorPickerView.ColorModel"));
        AutomationProperties.SetName(hue, L10n.Get("ColorPickerView.Hue")); AutomationProperties.SetName(alpha, L10n.Get("ColorPickerView.Opacity"));
        AutomationProperties.SetName(spectrum, L10n.Get("ColorPickerView.SaturationAndBrightness")); AutomationProperties.SetHelpText(spectrum, L10n.Get("ColorPickerView.ArrowKeysChangeSaturationAndBrightnessShift"));
        hue.Ramp = Gradient((0, Colors.Red), (.1667, Colors.Yellow), (.3333, Colors.Lime), (.5, Colors.Cyan), (.6667, Colors.Blue), (.8333, Colors.Magenta), (1, Colors.Red));
        spectrum.Changed += () => FromHsv(); hue.PropertyChanged += (_, args) => { if (args.Property == ColorAxis.ValueProperty && !updating) FromHsv(); };
        alpha.PropertyChanged += (_, args) => { if (args.Property == ColorAxis.ValueProperty && !updating) FromHsv(); };
        format.SelectionChanged += (_, _) => UpdateFields();
        hex.TextChanged += (_, _) =>
        {
            if (updating || hex.Text == synchronizedHex) return;
            var value = (hex.Text ?? "").Trim().TrimStart('#');
            if ((value.Length == 6 || value.Length == 8) && Color.TryParse("#" + value, out var parsed)) { SetColor(parsed, false); problem.IsVisible = false; }
            else { problem.Text = L10n.Get("ColorPickerView.HEXEnterRGBDigitsOrARGBDigits"); problem.IsVisible = true; }
        };
        SetColor(initial);
    }
    private static Control AxisRow(string name, ColorAxis slider)
    {
        var row = new Grid { ColumnDefinitions = new("126,*"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(slider, 1); row.Children.Add(slider); return row;
    }
    internal static LinearGradientBrush Gradient(params (double Position, Color Color)[] stops) => new()
    {
        StartPoint = new(0, .5, RelativeUnit.Relative), EndPoint = new(1, .5, RelativeUnit.Relative),
        GradientStops = [.. stops.Select(s => new GradientStop(s.Color, s.Position))]
    };
    public Color GetColor() { if (problem.IsVisible) throw new InvalidOperationException(problem.Text); return SelectedColor; }
    public void SetColor(Color color, bool updateHex = true) => UpdateColor(color, false, updateHex);
    private void FromHsv()
    {
        if (updating) return;
        spectrum.Hue = hue.Value; spectrum.InvalidateVisual();
        UpdateColor(new HsvColor(alpha.Value / 100, hue.Value, spectrum.Saturation, spectrum.Brightness).ToRgb(), true, true);
    }
    private void FromChannels()
    {
        if (updating) return;
        Color color = format.SelectedIndex == 1
            ? new HsvColor(alpha.Value / 100, (double)(channels[0].Value ?? 0), (double)(channels[1].Value ?? 0) / 100, (double)(channels[2].Value ?? 0) / 100).ToRgb()
            : Color.FromArgb(SelectedColor.A, (byte)(channels[0].Value ?? 0), (byte)(channels[1].Value ?? 0), (byte)(channels[2].Value ?? 0));
        SetColor(color);
    }
    private void UpdateColor(Color color, bool preserveHsv, bool updateHex)
    {
        updating = true;
        try
        {
            SelectedColor = color; var hsv = color.ToHsv();
            if (!preserveHsv) { if (hsv.S > 0) hue.Value = hsv.H; spectrum.Hue = hue.Value; spectrum.Saturation = hsv.S; spectrum.Brightness = hsv.V; }
            alpha.Value = color.A * 100.0 / 255; spectrum.InvalidateVisual();
            if (updateHex) hex.Text = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            synchronizedHex = hex.Text;
            problem.IsVisible = false; swatch.Current = color; swatch.InvalidateVisual();
            alpha.Ramp = Gradient((0, Color.FromArgb(0, color.R, color.G, color.B)), (1, Color.FromRgb(color.R, color.G, color.B))); alpha.InvalidateVisual();
            UpdateFields();
        }
        finally { updating = false; }
        ColorChanged?.Invoke(color);
    }
    private void UpdateFields()
    {
        var previous = updating; updating = true;
        try
        {
            var hsv = SelectedColor.ToHsv(); var useHsv = format.SelectedIndex == 1;
            var labels = useHsv ? new[] { "H · °", "S · %", "V · %" } : new[] { "R", "G", "B" };
            var values = useHsv ? new[] { hsv.H, hsv.S * 100, hsv.V * 100 } : new double[] { SelectedColor.R, SelectedColor.G, SelectedColor.B };
            for (var i = 0; i < channels.Length; i++) { names[i].Text = labels[i]; channels[i].Maximum = useHsv ? i == 0 ? 360 : 100 : 255; channels[i].Value = (decimal)Math.Round(values[i]); AutomationProperties.SetName(channels[i], labels[i]); }
        }
        finally { updating = previous; }
    }
}

internal sealed class ColorSpectrum : Control
{
    private bool dragging;
    internal double Hue, Saturation, Brightness;
    public event Action? Changed;
    public ColorSpectrum() { Focusable = true; Cursor = new(StandardCursorType.Cross); ClipToBounds = true; GotFocus += (_, _) => InvalidateVisual(); LostFocus += (_, _) => InvalidateVisual(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
    internal void Select(double saturation, double brightness) { Saturation = Math.Clamp(saturation, 0, 1); Brightness = Math.Clamp(brightness, 0, 1); InvalidateVisual(); Changed?.Invoke(); }
    private void TrackPointer(Point point) => Select(point.X / Math.Max(1, Bounds.Width), 1 - point.Y / Math.Max(1, Bounds.Height));
    protected override void OnPointerPressed(PointerPressedEventArgs args) { if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); dragging = true; args.Pointer.Capture(this); TrackPointer(args.GetPosition(this)); args.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs args) { if (dragging) { TrackPointer(args.GetPosition(this)); args.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs args) { dragging = false; args.Pointer.Capture(null); args.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs args) { dragging = false; base.OnPointerCaptureLost(args); }
    protected override void OnKeyDown(KeyEventArgs args)
    {
        var step = args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : .01;
        switch (args.Key) { case Key.Left: Select(Saturation - step, Brightness); break; case Key.Right: Select(Saturation + step, Brightness); break; case Key.Up: Select(Saturation, Brightness + step); break; case Key.Down: Select(Saturation, Brightness - step); break; default: base.OnKeyDown(args); return; }
        args.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size); if (area.Width < 1 || area.Height < 1) return;
        using (context.PushClip(new RoundedRect(area, 6)))
        {
            context.DrawRectangle(ColorPickerView.Gradient((0, Colors.White), (1, new HsvColor(1, Hue, 1, 1).ToRgb())), null, area);
            context.DrawRectangle(new LinearGradientBrush { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative), GradientStops = [new(Colors.Transparent, 0), new(Colors.Black, 1)] }, null, area);
        }
        if (IsKeyboardFocusWithin) context.DrawRectangle(null, new Pen(DockTheme.Accent, 1.5), area.Deflate(1), 5, 5);
        var at = new Point(Math.Clamp(Saturation * area.Width, 5, Math.Max(5, area.Width - 5)), Math.Clamp((1 - Brightness) * area.Height, 5, Math.Max(5, area.Height - 5)));
        context.DrawEllipse(null, new Pen(Brushes.Black, 3), at, 5, 5); context.DrawEllipse(null, new Pen(Brushes.White, 1.5), at, 5, 5);
    }
}

internal sealed class ColorAxis : Slider
{
    private bool dragging;
    internal IBrush? Ramp;
    internal bool Checkerboard;
    public ColorAxis() { Height = 22; MinHeight = 22; Focusable = true; Cursor = new(StandardCursorType.Hand); Template = new FuncControlTemplate<Slider>((_, _) => new Border()); }
    private void TrackPointer(Point point) { Value = Minimum + Math.Clamp((point.X - 7) / Math.Max(1, Bounds.Width - 14), 0, 1) * (Maximum - Minimum); InvalidateVisual(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) { base.OnPropertyChanged(change); if (change.Property == ValueProperty) InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs args) { if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); dragging = true; args.Pointer.Capture(this); TrackPointer(args.GetPosition(this)); args.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs args) { if (dragging) { TrackPointer(args.GetPosition(this)); args.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs args) { dragging = false; args.Pointer.Capture(null); args.Handled = true; }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs args) { dragging = false; base.OnPointerCaptureLost(args); }
    protected override void OnKeyDown(KeyEventArgs args)
    {
        var step = args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (args.Key) { case Key.Left: case Key.Down: Value = Math.Max(Minimum, Value - step); break; case Key.Right: case Key.Up: Value = Math.Min(Maximum, Value + step); break; case Key.Home: Value = Minimum; break; case Key.End: Value = Maximum; break; default: base.OnKeyDown(args); return; }
        InvalidateVisual(); args.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        var track = new Rect(7, 6, Math.Max(1, Bounds.Width - 14), 10);
        using (context.PushClip(new RoundedRect(track, 5))) { if (Checkerboard) ColorSwatch.Checkers(context, track); context.DrawRectangle(Ramp, null, track); }
        var x = 7 + (Value - Minimum) / Math.Max(1, Maximum - Minimum) * track.Width;
        context.DrawEllipse(Brushes.White, new Pen(IsKeyboardFocusWithin ? DockTheme.Accent : Brushes.Gray, 1.5), new Point(x, 11), 6, 6);
    }
}

internal sealed class ColorSwatch : Control
{
    private readonly Color original;
    internal Color Current;
    public ColorSwatch(Color initial) { original = Current = initial; }
    internal static void Checkers(DrawingContext context, Rect area)
    {
        for (var y = 0; y < area.Height; y += 8) for (var x = 0; x < area.Width; x += 8) context.DrawRectangle((x / 8 + y / 8) % 2 == 0 ? Brushes.LightGray : Brushes.White, null, new Rect(area.X + x, area.Y + y, Math.Min(8, area.Width - x), Math.Min(8, area.Height - y)));
    }
    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size); using (context.PushClip(new RoundedRect(area, 5))) { Checkers(context, area); context.DrawRectangle(new SolidColorBrush(original), null, new Rect(0, 0, area.Width / 2, area.Height)); context.DrawRectangle(new SolidColorBrush(Current), null, new Rect(area.Width / 2, 0, area.Width / 2, area.Height)); }
    }
}