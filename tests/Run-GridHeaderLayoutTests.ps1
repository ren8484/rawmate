$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $repoPath ('test-output\grid-header-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outPath | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runner = Join-Path $outPath 'GridHeaderLayoutTests.exe'
& "$framework\csc.exe" /nologo /target:exe /main:GridHeaderLayoutTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GridHeaderLayoutTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if ($LASTEXITCODE -ne 0) { throw 'Grid header tests compile failed' }
& $runner
if ($LASTEXITCODE -ne 0) { throw 'Grid header layout regression failed' }
