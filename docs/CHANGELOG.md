# Changelog

## Unreleased

- Remember the last active panel per tab. Add recent-window navigation and configurable global shortcuts.
- Show per-session agent activity with a jump to the owning window and optional Windows notifications.
- Preview project opening, reconnect existing windows, and start missing commands with optional resume arguments.
- Filter and preview collection, inspect results per window, and create capture rules from a panel.
- Add adaptive and stacked layouts, display profiles, and a command to return all windows to the desktop.

## 0.1.0 — Initial public version

WindowDock's public release history starts at 0.1.0. The earlier 2.x/3.x numbers belonged to private development builds; existing workspace data and integrations remain compatible.

- Group existing Windows applications in named tabs, with automatic overflow and configurable limits of 1–25 windows per tab.
- Arrange panels in a grid, with a main window on the left, or with two main windows on top. Resize, zoom, reorder, select, move across tabs, search, and undo supported actions.
- Restore groups of running windows and manage ambiguous matches explicitly.
- Capture new windows using application/title rules and priority exclusions.
- Launch saved projects and configure project-scoped Claude Code, Codex, and Gemini CLI status hooks.
- Preview Graphite, Light, Frosted glass, and Clear glass themes; choose a color, image, GIF, or transparent background for a tab.
- Select the interface language from the OS display locale, with eleven complete catalogs and English fallback.
- Use the .NET 10 LTS / C# 14 / Avalonia interface with a compact command row and independent native application processes.
- Include optimized placement, background decoding, autosaving, local IPC, and native recovery.
- Separate hook-protocol compatibility from the public release number. Preserve already installed callbacks through a monotonic Windows file counter.
- Add `--version` and display the public version in Help.
- Provide MIT-licensed source, English documentation, contribution guidance, issue forms, CI, and portable Windows packaging.

See [versioning](VERSIONING.md), [performance notes](../OPTIMIZATION.md), and the [roadmap](ROADMAP.md).
