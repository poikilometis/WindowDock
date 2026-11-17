# Roadmap

WindowDock currently provides native docking on Windows x64. Roadmap items are directions, not release dates or promises of platform parity.

## Current foundation

- Shared workspace, layout, rule, restore, project, and agent-routing core.
- .NET 10 / C# 14 and an Avalonia desktop interface.
- Windows native backend isolated from the shared model.
- Eleven interface locales selected from the OS display language.
- Portable packaging, local recovery, test fixtures, and project-scoped hook adapters.

## Cross-platform work

1. Prototype what can actually be controlled on macOS and Wayland before designing a common feature matrix. True foreign-window embedding is not universally available.
2. Define explicit backend capabilities for docking, arranging external windows, focus, scrolling, clipping, transparency, hotkeys, and restoration.
3. Implement a Linux X11 backend and investigate compositor-specific Wayland integrations. Offer clear capability limits where embedding is unavailable.
4. Implement a macOS backend using supported window-management/accessibility APIs. Design permissions and window-group behavior around platform constraints.
5. Adapt process/session discovery, project launch, shell quoting, and local agent transport. Remove mandatory Windows PowerShell and HWND assumptions from portable paths.
6. Add platform-specific package formats, signing/notarization where applicable, native dependencies, CI, and real desktop testing for each supported OS and architecture.

## Windows and product quality

- Broaden the application/DPI/IME compatibility matrix with reproducible reports.
- Improve diagnostics and recovery for unusual GPU-rendered and elevated windows.
- Gather native-speaker review for translations and refine accessibility across all languages.
- Keep expensive visuals, discovery, and persistence outside interaction hot paths.

The existing Windows implementation remains the reference for native-window behavior. Building the Avalonia shell on another OS does not imply that it can dock that OS's applications.