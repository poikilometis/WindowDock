param([Parameter(Mandatory=$true)][string]$Directory, [string]$Packages=(Join-Path (Split-Path -Parent $PSScriptRoot) '.packages'))
$ErrorActionPreference='Stop'
$deps=Get-Content -LiteralPath (Join-Path $Directory 'WindowDock.deps.json') -Raw | ConvertFrom-Json
$text=New-Object Text.StringBuilder
[void]$text.AppendLine('WindowDock — third-party package notices')
foreach($entry in $deps.libraries.PSObject.Properties | Where-Object {$_.Value.type -eq 'package'}) {
    $parts=$entry.Name.Split('/')
    $folder=Join-Path (Join-Path $Packages $parts[0].ToLowerInvariant()) $parts[1]
    [void]$text.AppendLine(''); [void]$text.AppendLine($entry.Name)
    [void]$text.AppendLine(('https://www.nuget.org/packages/'+$parts[0]+'/'+$parts[1]))
    if(Test-Path -LiteralPath $folder) {
        foreach($file in Get-ChildItem -LiteralPath $folder -File | Where-Object {$_.Name -match '(?i)license|copyright|notice|copying'}) { [void]$text.AppendLine([IO.File]::ReadAllText($file.FullName)) }
    }
}
[IO.File]::WriteAllText((Join-Path $Directory 'THIRD-PARTY-NOTICES.txt'),$text.ToString(),[Text.UTF8Encoding]::new($false))