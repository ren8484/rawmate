param([Parameter(Mandatory=$true)][string]$ExifToolPath,[Parameter(Mandatory=$true)][string]$WorkingJpgFolder,[Parameter(Mandatory=$true)][string]$BaselineRoot)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outputDir=Join-Path $repoPath ('test-output\jpg-evidence-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDir | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler -Parent
$runner=Join-Path $outputDir 'JpgGpsEvidenceTests.exe'
& $compiler /nologo /target:exe /main:JpgGpsEvidenceTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsTestFixture.cs') (Join-Path $PSScriptRoot 'JpgGpsEvidenceTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'JPG evidence compilation failed'}
& $runner $ExifToolPath $WorkingJpgFolder (Join-Path $outputDir 'integrity-evidence.txt') $BaselineRoot
if($LASTEXITCODE -ne 0){throw 'JPG evidence gates failed'}
