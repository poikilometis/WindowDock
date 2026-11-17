# Publishing and delivery

WindowDock uses GitHub Actions for validation and GitHub Releases for desktop distribution. Native application docking currently ships for Windows x64.

## CI

Every push to `main` and every pull request runs the shared core tests on Windows, Linux, and macOS. The Windows job also builds the application, runs native/locale integration tests, packages the portable distribution and committed source, and tests the extracted ZIP. Reports and archives are retained as workflow artifacts for 14 days. The final **Required checks** job fails if any required job fails.

`ci.yml` is reusable: the tag release calls the same jobs instead of maintaining a second implementation. Actions are pinned to reviewed commit SHAs; Dependabot proposes package/action updates. Workflow tokens default to read-only access. Release build attestation and publication jobs receive only their specific additional permissions.

## A release

1. Set `Version` in `Directory.Build.props`, increase the separate Windows file counter, and update `docs/CHANGELOG.md`.
2. Run `dotnet restore WindowDock.sln --force-evaluate`, commit refreshed lock files, and run `./build.ps1 -Test`.
3. Merge the reviewed change into `main` after CI succeeds.
4. Create an annotated tag matching the version, for example `git tag -a v0.1.0 -m "WindowDock 0.1.0"`, and push that exact tag.
5. The release workflow validates the tag, repeats all required checks, generates provenance attestations, and publishes the verified ZIPs and SHA-256 files to GitHub Releases. Beta/RC tags produce prereleases; normal tags update the latest stable release.

Published assets and version tags are immutable. Fixes receive a new version. Deployment is recorded in the `github-release` environment, restricted to version tags.

The release notes come from the matching changelog entry. Windows archives are currently unsigned; provenance attestations identify the build workflow and do not replace Windows code signing.

## Initial repository setup

The maintainer helper `tools/Publish-GitHub.ps1 -Release` creates the public repository if needed, pushes the already committed source, configures metadata and Actions, enables security notifications/private reporting, waits for CI, protects `main`, restricts release deployment to tags, and creates/pushes the release tag. It never force-pushes or silently changes an existing repository's visibility.

Use GitHub CLI authenticated as the repository owner with repository and workflow permissions. `-GhConfigDirectory <folder>` selects an existing dedicated CLI profile. No token is written into source or command arguments. Run this helper from a normal PowerShell with working Git/GitHub networking; native desktop build scripts use PowerShell 7.

Main requires passing checks, resolved review conversations, and pull requests; zero approvals support a solo maintainer. Administrators retain the normal recovery bypass. Version-tag rules prevent updates/deletion. Repository Actions allow official GitHub actions, with default read-only tokens and no PR approval by workflows.

Before first publication, review the entire source inventory, license and screenshots. Exclude local workspace data, caches, hook backups, and internal notes. Enable branch/tag protections only after the initial checked commit is available.