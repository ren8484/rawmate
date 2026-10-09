param([Parameter(Mandatory=$true)][string]$ExifToolPath,[Parameter(Mandatory=$true)][string]$BaselineRoot,[Parameter(Mandatory=$true)][string]$CreateGpsIfdPairs)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outputDir=Join-Path $repoPath ('test-output\jpg-integration-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDir | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler -Parent
$runner=Join-Path $outputDir 'JpgGpsIntegrationTests.exe'
& $compiler /nologo /target:exe /main:JpgGpsIntegrationTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsTestFixture.cs') (Join-Path $PSScriptRoot 'JpgGpsIntegrationTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'JPG integration compilation failed'}
& $runner (Join-Path $outputDir 'working') $ExifToolPath $BaselineRoot $CreateGpsIfdPairs
if($LASTEXITCODE -ne 0){throw 'JPG integration failed; evidence retained'}
Write-Output "WORKING_JPG=$(Join-Path $outputDir 'working\jpg')"
