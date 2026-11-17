param([string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive = [IO.Path]::GetFullPath((Join-Path $OutputDirectory "WindowDock-$version-source.zip"))
if (Test-Path -LiteralPath $archive) { throw 'Use a fresh source-archive output path.' }
$committedProps = & git -C $root show HEAD:Directory.Build.props
if ($LASTEXITCODE -ne 0) { throw 'Source packaging requires a Git commit.' }
if (([xml]($committedProps -join "`n")).Project.PropertyGroup.Version -ne $version) { throw 'Commit the version change before packaging.' }
& git -C $root archive --format=zip --prefix=WindowDock/ --output=$archive HEAD
if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ($zip.Entries | Where-Object { $_.FullName -match '(^|/)(data|\.git|\.packages|\.tools|artifacts|bin|obj)/' }) { throw 'Generated/private paths in source archive.' }
} finally { $zip.Dispose() }
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Packaged committed source: $archive"