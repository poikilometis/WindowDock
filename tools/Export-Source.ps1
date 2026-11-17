param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd([IO.Path]::DirectorySeparatorChar)
$outputRoot = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (Test-Path -LiteralPath $outputRoot) { throw 'Choose a new, empty export path. Existing directories are never replaced.' }
if ($outputRoot -eq $sourceRoot) { throw 'Source and destination must differ.' }
$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($name in @('.editorconfig', '.gitattributes', '.gitignore', 'LICENSE', 'README.md', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'SECURITY.md', 'OPTIMIZATION.md', 'Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.Config', 'WindowDock.sln', 'WindowDock.integration.json', 'app.ico', 'app.manifest', 'agent-status.ps1', 'build.ps1')) {
    $files.Add((Get-Item -LiteralPath (Join-Path $sourceRoot $name) -Force))
}
foreach ($directory in @('src', 'tests', 'tools', 'assets', 'docs', '.github')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $sourceRoot $directory) -File -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj)([\\/]|$)') { continue }
        if ($file.Extension -notin '.cs', '.csproj', '.resx', '.axaml', '.ps1', '.png', '.svg', '.json', '.yml', '.yaml', '.md' -and $relative.Replace('\','/') -ne '.github/CODEOWNERS') { throw "Unexpected source file: $relative" }
        $files.Add($file)
    }
}
# Validate the full inventory before writing any files.
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
    if ($relative -match '(^|[\\/])(data|artifacts|\.packages|\.tools|\.runtime|\.git|\.codex|\.aws|\.agents|\.impeccable|legacy)([\\/]|$)') { throw "Private/generated file in export: $relative" }
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null
foreach ($file in $files) {
    $target = Join-Path $outputRoot ([IO.Path]::GetRelativePath($sourceRoot, $file.FullName))
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Write-Host "Exported $($files.Count) source files to $outputRoot"