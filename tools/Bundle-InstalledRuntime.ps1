param([Parameter(Mandatory=$true)][string]$Destination, [string]$DotNetRoot='')
$ErrorActionPreference='Stop'
if ($DotNetRoot -eq '') { $DotNetRoot=Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source }
$sourceRoot=[IO.Path]::GetFullPath($DotNetRoot)
$outputRoot=[IO.Path]::GetFullPath($Destination)
$runtimeVersion=Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'shared\Microsoft.NETCore.App') -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+$' } | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
if (-not $runtimeVersion) { throw 'An installed .NET 10 x64 runtime is required for this portable layout.' }
$fxrSource=Join-Path $sourceRoot ('host\fxr\'+$runtimeVersion.Name)
if (-not(Test-Path -LiteralPath $fxrSource)) { throw ('Matching hostfxr not found: '+$fxrSource) }
$localRuntime=Join-Path $outputRoot '.runtime'
$frameworkDestination=Join-Path $localRuntime ('shared\Microsoft.NETCore.App\'+$runtimeVersion.Name)
$fxrDestination=Join-Path $localRuntime ('host\fxr\'+$runtimeVersion.Name)
New-Item -ItemType Directory -Path $frameworkDestination,$fxrDestination -Force | Out-Null
Get-ChildItem -LiteralPath $runtimeVersion.FullName -File | Copy-Item -Destination $frameworkDestination -Force
Get-ChildItem -LiteralPath $fxrSource -File | Copy-Item -Destination $fxrDestination -Force
foreach($name in 'dotnet.exe','LICENSE.txt','ThirdPartyNotices.txt') {
    $source=Join-Path $sourceRoot $name
    if(Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $localRuntime -Force }
}
[IO.File]::WriteAllText((Join-Path $localRuntime 'README.txt'),"Official Microsoft .NET runtime $($runtimeVersion.Name), copied unmodified from the installed distribution. The application is published with AppHostDotNetSearch=AppRelative and AppHostRelativeDotNet=.runtime; it does not require a global .NET installation. Keep this directory beside WindowDock.exe.")
Write-Host ('Bundled official local .NET runtime '+$runtimeVersion.Name+' at '+$localRuntime)