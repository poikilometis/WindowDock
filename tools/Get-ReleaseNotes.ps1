param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$changelog = [IO.File]::ReadAllText((Join-Path $root 'docs\CHANGELOG.md')).Replace("`r`n", "`n")
$pattern = '(?ms)^##[ \t]+' + [regex]::Escape($version) + '(?:[ \t]+[^\n]*)?\n(.*?)(?=^##[ \t]|\z)'
$entry = [regex]::Match($changelog, $pattern)
if (-not $entry.Success) { throw "No changelog entry for $version." }
$downloadNotes = @(
    '## Downloads', '',
    '- **Windows x64 portable ZIP:** extract the entire archive, then run `WindowDock.exe`. The official .NET runtime and all interface locales are included.',
    '- **Source ZIP:** committed source for this release.',
    '- **SHA-256 files:** verify each archive before use. Build provenance is available through GitHub artifact attestations.', '',
    'Native docking currently supports Windows x64. Linux and macOS native backends remain on the roadmap. This Windows build is unsigned.'
) -join "`n"
$body = $entry.Groups[1].Value.Trim() + "`n`n" + $downloadNotes
$path = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
[IO.File]::WriteAllText($path, $body + "`n", [Text.UTF8Encoding]::new($false))