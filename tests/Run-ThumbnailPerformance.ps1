param([Parameter(Mandatory=$true)][string]$JpgFolder)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$testExe = Join-Path $repoPath 'RAWMate-Test.exe'
$outputDir = Join-Path $repoPath 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$runnerPath = Join-Path $outputDir 'ThumbnailPerformanceTests.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compilerPath /nologo /target:exe "/out:$runnerPath" (Join-Path $PSScriptRoot 'ThumbnailPerformanceTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Thumbnail performance harness compilation failed' }
& $runnerPath $testExe (Resolve-Path -LiteralPath $JpgFolder)
if ($LASTEXITCODE -ne 0) { throw 'Thumbnail performance regression failed' }
