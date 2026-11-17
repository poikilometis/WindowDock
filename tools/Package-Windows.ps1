param(
    [string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\WindowDock'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$source = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $source 'WindowDock.exe'))) { throw 'Build the portable distribution first.' }
foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.txt', 'WindowDock.integration.json', 'ru', 'es', 'vi', 'zh-Hans', 'zh-Hant', 'th', 'ja', 'ko', 'de', 'tr')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required))) { throw "Missing distribution asset: $required" }
}
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'WindowDock.exe'))
if ($metadata.ProductVersion.Split('+')[0] -ne $version) { throw "The executable product version does not match $version." }
$protocol = Get-Content -LiteralPath (Join-Path $source 'WindowDock.integration.json') -Raw | ConvertFrom-Json
if ($protocol.product -ne 'WindowDock' -or $protocol.hookProtocol -ne 1) { throw 'Unsupported hook-protocol manifest.' }
if (Test-Path -LiteralPath (Join-Path $source 'data')) { throw 'A release package must not contain a user data folder.' }
if ($destination -eq $source -or $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The ZIP must be outside the packaged folder.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archive = Join-Path $destination "WindowDock-$version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive. Choose a fresh output directory." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# ZipFile includes hidden .runtime files, unlike Compress-Archive on Windows.
[IO.Compression.ZipFile]::CreateFromDirectory($source, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ((Test-Path -LiteralPath (Join-Path $source '.runtime')) -and -not ($zip.Entries | Where-Object FullName -like '.runtime/*')) { throw 'The archive is missing the bundled runtime.' }
    if ($zip.Entries | Where-Object FullName -like 'data/*') { throw 'The archive contains private workspace data.' }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Packaged $archive"