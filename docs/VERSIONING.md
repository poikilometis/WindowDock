# Versioning

Public WindowDock releases start at **0.1.0** and use `MAJOR.MINOR.PATCH`. The private 2.x/3.x build numbers are historical development identifiers, not earlier public releases.

## Before 1.0

- `0.1.0`: initial public version.
- `0.1.1`: compatible bug fixes to the 0.1 series.
- `0.2.0`: new functionality or a substantial change; reset PATCH when MINOR increases.
- `0.25.3`: patch 3 of the 0.25 series. Components are integers; 0.10 follows 0.9.
- `0.2.0-beta.1` / `0.2.0-rc.1`: optional test releases before 0.2.0.

A breaking change before 1.0 increments MINOR and is called out explicitly in the changelog. Existing workspace data should still receive a safe migration where practical. The 1.0 milestone means the public CLI, integration contracts, data migration rules, and supported Windows workflows have a documented stability commitment. This follows the [SemVer initial-development model](https://semver.org/#spec-item-4).

## Sources and metadata

`Directory.Build.props` is the authoritative source of the public version. Assembly identity, product/informational metadata, Help, `--version`, portable archive names, and the release tag use it. The desktop build fills the Win32 application-manifest version from the SDK's derived assembly version; `app.manifest` is a template.

Windows **FileVersion is an independent, monotonic compatibility counter**. It remains in the private 3.x range because previously installed hooks refuse executables whose file version is below 2.1. Resetting that field to 0.1 would silently disable those callbacks. `0.1.0` therefore uses product version `0.1.0`, assembly/Win32 identity `0.1.0.0`, and file counter `3.4.1.0`. Increase the file counter for subsequent distributed builds; users should identify releases by product version.

New callbacks read `WindowDock.integration.json` beside the executable and require its advertised hook protocol. That protocol changes only when the integration contract changes, independently of the release number. Missing or unsupported manifests produce neutral hook output without launching a GUI or forwarding an event.

The saved workspace format remains v4. Its schema version is independent of both release and hook-protocol versions.

## Release policy

Use an exact Git tag such as `v0.1.0`; tags must match `Directory.Build.props`. Prerelease tags include the same suffix as the product version. Released archives are immutable: issue another version rather than replacing a published build.

Update the changelog, run the core and Windows integration checks, and package into a clean folder. Packaging verifies the executable product version and hook manifest before naming the archive. See [PUBLISHING.md](PUBLISHING.md).