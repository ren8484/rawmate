param([string]$ExifToolPath)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$outputDir = Join-Path $repoPath 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$runnerPath = Join-Path $outputDir 'GpsModelTests.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Split-Path $compilerPath -Parent
& $compilerPath /nologo /target:exe /main:GpsModelTests "/out:$runnerPath" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsModelTests.cs') "/reference:$frameworkPath\WPF\WindowsBase.dll" "/reference:$frameworkPath\WPF\PresentationCore.dll" "/reference:$frameworkPath\WPF\PresentationFramework.dll" "/reference:$frameworkPath\System.Xaml.dll" /reference:System.Windows.Forms.dll
if ($LASTEXITCODE -ne 0) { throw 'GPS test harness compilation failed' }
$fixturePath = Join-Path $outputDir ('gps-model-' + [Guid]::NewGuid().ToString('N'))
if ($ExifToolPath) { & $runnerPath $fixturePath $ExifToolPath }
else { & $runnerPath $fixturePath }
if ($LASTEXITCODE -ne 0) { throw 'GPS model/adapter/transaction regression failed' }
