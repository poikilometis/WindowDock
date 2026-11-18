namespace WindowDock;

internal readonly record struct DockShortcut(uint Modifiers, uint Key)
{
    internal static bool TryParse(string text, out DockShortcut shortcut)
    {
        shortcut = default;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2) return false;
        uint modifiers = 0;
        foreach (var token in tokens[..^1])
        {
            var flag = token.ToUpperInvariant() switch
            {
                "ALT" => 1u, "CTRL" => 2u, "SHIFT" => 4u, "WIN" => 8u, _ => 0u
            };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var last = tokens[^1].ToUpperInvariant();
        uint key = last switch
        {
            "ENTER" => 0x0D, "SPACE" => 0x20, "TAB" => 0x09,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            _ when last.Length == 1 && (last[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') => last[0],
            _ when last.StartsWith('F') && int.TryParse(last.AsSpan(1), out var function) && function is >= 1 and <= 24 => (uint)(0x6F + function),
            _ => 0u
        };
        if (key == 0) return false;
        shortcut = new DockShortcut(modifiers | 0x4000, key); // MOD_NOREPEAT
        return true;
    }
}

internal sealed record DockCommand(int Id, string Label, string Default);

internal static class DockShortcuts
{
    internal static IReadOnlyList<DockCommand> Commands { get; } =
    [
        ..Enumerable.Range(1, 9).Select(index => new DockCommand(100 + index, "Models.Tab", $"Ctrl+Alt+{index}")),
        new(120, "MainWindowCommands.Zoom", "Ctrl+Alt+Enter"),
        new(121, "MainWindow.AddWindows", "Ctrl+Alt+A"),
        new(122, "MainWindowCommands.MinimizeGroup", "Ctrl+Alt+D"),
        new(123, "Workflow.ShowDock", "Ctrl+Alt+G"),
        new(124, "MainWindowCommands.FindWindow", "Ctrl+Alt+K"),
        new(125, "MainWindowCommands.UndoAction", "Ctrl+Alt+Z"),
        new(126, "Workflow.ToggleDock", "Ctrl+Alt+Space"),
        new(127, "Workflow.PreviousWindow", "Ctrl+Alt+B"),
        new(128, "Workflow.NextAgent", "Ctrl+Alt+J")
    ];

    internal static string Value(AppConfig config, DockCommand command) => config.Shortcuts.GetValueOrDefault(command.Id) ?? command.Default;
    internal static string Label(DockCommand command) => command.Id is >= 101 and <= 109
        ? $"{L10n.Get(command.Label)} {command.Id - 100}"
        : L10n.Get(command.Label);
}
