param([Parameter(Mandatory=$true)][string]$ExifToolPath,[Parameter(Mandatory=$true)][string]$BaselineRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$out=Join-Path $repo ('test-output\recursive-directory-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $csc -Parent
$runner=Join-Path $out 'RecursiveDirectory-Test.exe'
& $csc /nologo /target:exe /main:RecursiveDirectoryTests "/out:$runner" (Join-Path $repo 'RAWMate.cs') (Join-Path $PSScriptRoot 'RecursiveDirectoryTests.cs') (Join-Path $PSScriptRoot 'GpsDirectoryRefreshTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'Recursive directory test compile failed'}
& $runner (Join-Path $out 'working') $ExifToolPath $BaselineRoot | Tee-Object -FilePath (Join-Path $out 'results.txt')
if($LASTEXITCODE -ne 0){throw 'Recursive directory regression failed; evidence retained'}
