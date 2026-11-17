param(
    [switch]$Test,
    [switch]$Publish,
    [switch]$NoRestore,
    [switch]$FrameworkDependent,
    [switch]$BundleInstalledRuntime,
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'Install .NET 10 SDK: choco install dotnet-10.0-sdk -y' }
$sdks = & $dotnet.Source --list-sdks
if (-not ($sdks -match '^10\.0\.')) { throw '.NET 10 SDK is required. Install: choco install dotnet-10.0-sdk -y' }
$iconPath = Join-Path $projectRoot 'app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'tools\Build-Icon.ps1') -ProjectRoot $projectRoot }
$desktopProject = Join-Path $projectRoot 'src\WindowDock.Desktop\WindowDock.Desktop.csproj'
$solution = Join-Path $projectRoot 'WindowDock.sln'
$restoreArgs = @()
if ($NoRestore) { $restoreArgs = @('--no-restore') }
& $dotnet.Source build $solution -c Release --nologo @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
if ($Test) {
    & $dotnet.Source run --project (Join-Path $projectRoot 'tests\WindowDock.Tests\WindowDock.Tests.csproj') -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Core checks failed ($LASTEXITCODE)" }
    $nativeBuild = Join-Path $projectRoot 'src\WindowDock.Desktop\bin\Release\net10.0\WindowDock.exe'
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $probe = Start-Process -FilePath $nativeBuild -ArgumentList '--self-test' -WorkingDirectory $projectRoot -WindowStyle Hidden -Wait -PassThru
        Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\avalonia-tests.txt')
        if ($probe.ExitCode -ne 0) { throw "Avalonia checks failed ($($probe.ExitCode))" }
    }
}
if ($OutputDirectory -eq '') { $OutputDirectory = if ($Publish) { Join-Path $projectRoot 'dist\WindowDock' } else { Join-Path $projectRoot 'artifacts\modern' } }
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
if ($Publish) {
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    if ($BundleInstalledRuntime) {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Bundling the installed runtime is currently provided for Windows x64.' }
        & $dotnet.Source publish $desktopProject -c Release --self-contained false -p:UseAppHost=true -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=.runtime -p:PublishTrimmed=false -o $resolvedOutput --nologo @restoreArgs
        if ($LASTEXITCODE -eq 0) {
            & (Join-Path $projectRoot 'tools\Bundle-InstalledRuntime.ps1') -Destination $resolvedOutput
            & (Join-Path $projectRoot 'tools\Trim-WindowsAssets.ps1') -Directory $resolvedOutput
        }
    } else {
        & $dotnet.Source publish $desktopProject -c Release -r $RuntimeIdentifier --self-contained $selfContained -p:PublishSingleFile=false -p:PublishTrimmed=false -o $resolvedOutput --nologo @restoreArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE). Restore runtime packages: dotnet restore src\WindowDock.Desktop\WindowDock.Desktop.csproj -r $RuntimeIdentifier" }
} else {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\WindowDock.Desktop\bin\Release\net10.0') | Copy-Item -Destination $resolvedOutput -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $resolvedOutput -Force
foreach ($document in @('OPTIMIZATION.md', 'LICENSE', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $resolvedOutput -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $resolvedOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $resolvedOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CONTRIBUTING.md') -Destination $resolvedOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CODE_OF_CONDUCT.md') -Destination $resolvedOutput -Force
Copy-Item -LiteralPath $iconPath -Destination $resolvedOutput -Force
& (Join-Path $projectRoot 'tools\Collect-Licenses.ps1') -Directory $resolvedOutput -Packages $env:NUGET_PACKAGES
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    & (Join-Path $projectRoot 'tools\Refresh-Icons.ps1') -Paths @($iconPath,(Join-Path $resolvedOutput 'WindowDock.exe'))
}
$builtVersion = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
Write-Host "WindowDock $builtVersion built: $resolvedOutput"