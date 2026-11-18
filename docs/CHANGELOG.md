# Changelog

## 0.2.2 — Make the workspace yours and stay up to date

- **Style windows and tabs.** Set a solid color, a static gradient, or a flowing gradient for window cards, the toolbar, and tabs. Adjust the toolbar and compatible docked windows' opacity, pick a window title color, or customize individual panels and tabs. The Light Beams theme adds an animated workspace background.
- **Keep Windows awake while work is running.** Choose to prevent automatic sleep while WindowDock runs, while a window is docked, or while a connected agent reports that it is working. Windows can still turn off the display.
- **Go directly to an agent from its notification.** Clicking a Windows notification opens the source window and highlights its card for four seconds. Hook settings show a compact setup result, with the copyable preview available on demand.
- **Install future stable updates in WindowDock.** The toolbar's **Update** button checks GitHub Releases. The update card shows the version, opens the changelog, and offers installation. A dismissed card returns after 24 hours or at the next launch. Installation checks the archive and opens a **What's new** tab after restarting.

## 0.2.1 — Resume your workspace and keep windows in place

- **Pick up after a restart.** Reopen selected projects when WindowDock starts and resume linked Codex, Claude Code, and Gemini CLI conversations by ID. Restore Windows Terminal's saved tab and pane layout and keep optional text snapshots of terminal output.
- **Jump to the right agent.** Bind a session to a Windows Terminal tab and pane. The bell and `Ctrl+Alt+J` use that binding. Notification switches and setup feedback are now in the integration dialog.
- **Work across displays.** Open tabs in separate windows on different monitors or bind them to Windows virtual desktops.
- **Find and add windows from one search.** `Ctrl+Alt+K` searches docked panels and desktop windows. Capture rules can use executable paths and project folders reported by hooks.
- **Keep your layout in place.** Split and resize individual areas. Moving a window across tabs fills the first empty slot. Dropping one window onto another swaps the two positions.
- **Keep difficult apps within reach.** Link an app to a panel while it runs in its original window.
- **Scroll and resize terminals more reliably.** Inactive classic consoles can receive wheel input on hover, including in supported terminal UIs. Restored windows appear without a click, and scrollbars repaint after a resize.

## 0.2.0 — Agent activity and workspace controls

When several agents are running in separate terminals, you can now see who needs a reply and jump straight to their window. WindowDock also remembers where you were working when you switch tabs, so you can pick up from the same panel.

- **See which agent needs you.** The bell shows waiting sessions and errors across tabs, with an elapsed time for each session. Open an entry to focus its window, or press `Ctrl+Alt+J` to go to the next agent that needs attention. Enable Windows notifications from **View** if you want alerts while WindowDock is in the background.
- **Get back to the right window faster.** Each tab remembers its last active panel. Use `Ctrl+Alt+B` for the previous window and `Ctrl+Alt+Space` to bring the group forward or minimize it. Edit the bindings under **View → Global shortcuts**.
- **Reopen a project and keep its running sessions.** The preview shows which windows can be reused and which commands are missing. Review the matches, then choose whether to start the missing commands. Link an existing panel to a command from its menu. Commands can also use optional resume arguments supplied by their application.
- **Choose which windows to collect.** Right-click **Collect windows** for a preview, text and terminal filters, and the option to include minimized windows. Review the last result to see where each window went or why it was skipped. Create a capture rule or exclusion directly from a panel.
- **Keep windows readable when space is tight.** The adaptive grid switches to a stack when tiles would exceed the available height. Pin one window beside the stack when there is room. WindowDock remembers its placement for each display setup, and saved panels keep their positions when they reconnect in a different order.

The new controls are available in all eleven interface languages. Use **Return all windows and pause capture** any time you want to put your applications back on the desktop while their processes keep running.

### Upgrade from 0.1.0

Close WindowDock and back up the `data` folder. Extract the whole Windows archive, then replace the program files in your current WindowDock folder and keep `data` in place. Using the same executable path keeps your configured agent callbacks pointed at WindowDock.

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
