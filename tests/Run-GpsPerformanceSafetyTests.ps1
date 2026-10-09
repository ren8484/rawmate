param([Parameter(Mandatory=$true)][string]$BaselineRoot,[Parameter(Mandatory=$true)][string]$ExifToolPath,[string]$WorkingJpgFolder)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outPath=Join-Path $repoPath ('test-output\gps-performance-safety-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outPath | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler -Parent
$runner=Join-Path $outPath 'GpsPerformanceSafetyTests.exe'
& $compiler /nologo /optimize+ /target:exe /main:GpsPerformanceSafetyTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsTestFixture.cs') (Join-Path $PSScriptRoot 'GpsPerformanceSafetyTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'Safety harness compile failed'}
$testArgs=@((Join-Path $outPath 'working'),$ExifToolPath,$BaselineRoot)
if($WorkingJpgFolder){$testArgs+=$WorkingJpgFolder}
& $runner @testArgs | Tee-Object -FilePath (Join-Path $outPath 'safety.txt')
if($LASTEXITCODE -ne 0){throw 'Performance safety test failed'}
Write-Output "EVIDENCE=$outPath"
