param([switch]$UiFixture)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$testExe = Join-Path $repoPath 'RAWMate-Test.exe'
$outputDir = Join-Path $repoPath 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$runnerPath = Join-Path $outputDir 'CullModelTests.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64/v4.0.30319/csc.exe'
& $compilerPath /nologo /target:exe "/out:$runnerPath" (Join-Path $PSScriptRoot 'CullModelTests.cs') /reference:System.Drawing.dll
if ($LASTEXITCODE -ne 0) { throw 'Test harness compilation failed' }
$fixturePath = Join-Path $outputDir ('retention-' + [Guid]::NewGuid().ToString('N'))
if ($UiFixture) { & $runnerPath $testExe $fixturePath --ui-fixture }
else { & $runnerPath $testExe $fixturePath }
if ($LASTEXITCODE -ne 0) { throw 'Retention regression failed' }
