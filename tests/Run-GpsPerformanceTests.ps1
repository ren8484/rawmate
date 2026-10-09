param([Parameter(Mandatory=$true)][string]$BaselineRoot,[Parameter(Mandatory=$true)][string]$ExifToolPath,[string]$Mode='1',[string]$Label='run',[string]$SourcePath)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outPath=Join-Path $repoPath ('test-output\gps-performance-'+$Label+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outPath | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler -Parent
$runner=Join-Path $outPath 'GpsPerformanceTests.exe'
if(!$SourcePath){$SourcePath=Join-Path $repoPath 'RAWMate.cs'}
& $compiler /nologo /optimize+ /target:exe /main:GpsPerformanceTests "/out:$runner" $SourcePath (Join-Path $PSScriptRoot 'GpsTestFixture.cs') (Join-Path $PSScriptRoot 'GpsPerformanceTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'Performance harness compile failed'}
& $runner (Join-Path $outPath 'working') $ExifToolPath $Mode $BaselineRoot | Tee-Object -FilePath (Join-Path $outPath 'timings.txt')
if($LASTEXITCODE -ne 0){throw 'Performance test failed'}
Write-Output "EVIDENCE=$outPath"
