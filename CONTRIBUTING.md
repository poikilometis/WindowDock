# Contributing to WindowDock

Thanks for helping improve WindowDock. Small, reproducible bug reports and focused changes are useful contributions, as are translations and compatibility results for desktop applications.

## Before starting

Search existing issues. For a large feature or native backend, open an issue describing the user problem, proposed behavior, and platform limits before committing to an implementation. Read the [roadmap](docs/ROADMAP.md) and [code of conduct](CODE_OF_CONDUCT.md).

## Development

1. Fork and clone the repository on Windows x64.
2. Install the .NET 10 SDK and PowerShell.
3. Run `./build.ps1 -Test` from the repository root.
4. Use a separate profile for manual work: `WindowDock.exe --data-dir ./artifacts/dev-profile`.

`src/WindowDock.Desktop/bin/Release/net10.0/WindowDock.exe` is the developer executable. Run the core tests with `dotnet run --project tests/WindowDock.Tests -c Release`. Core checks can also run on Linux; native window integration tests require Windows with a graphical desktop.

## Changes and validation

- Keep the compact shell and the target application's own input behavior intact.
- Keep shared workspace logic separate from native window operations.
- Preserve compatibility with v4 settings and groups unless a migration is explicitly designed and tested.
- Add meaningful checks for changed behavior. Use disposable fixture windows for focus, scrolling, recovery, and move/resize scenarios.
- For UI changes, verify the minimum supported size, OS scaling, keyboard navigation, and long/non-Latin translations. Include sanitized before/after images when useful.
- For integration changes, preserve unrelated hooks, backup integrity, idempotence, and explicit agent trust. Do not send live model requests in automated tests.
- Put every user-facing application message in the resource catalogs. See [LOCALIZATION.md](docs/LOCALIZATION.md).

The desktop `--self-test` command creates and closes its own test applications, including an intentionally unresponsive fixture. Never point tests at your regular data folder. Test reports and screenshots are written under `artifacts/`.

## Pull requests

Explain the concrete problem, resulting behavior, and validation. Mention affected applications, privilege level, Windows version, display scaling, and any remaining compatibility limit. Avoid unrelated reformatting or dependency changes. Ensure local checks pass before requesting review.

Never commit `data/`, `artifacts/`, binaries, package caches, private project configuration, access tokens, agent conversations, or screenshots containing personal window titles. The supplied ignore rules help, but review your diff as well.

## License

By submitting a contribution, you agree that it may be distributed under the project's [MIT License](LICENSE). Preserve third-party attribution and identify externally sourced code or assets.