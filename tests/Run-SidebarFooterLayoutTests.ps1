$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $repoPath ('test-output\sidebar-footer-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outPath | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$runner = Join-Path $outPath 'SidebarFooterLayoutTests.exe'
& "$framework\csc.exe" /nologo /target:exe /main:SidebarFooterLayoutTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'SidebarFooterLayoutTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if ($LASTEXITCODE -ne 0) { throw 'Sidebar footer tests compile failed' }
& $runner
if ($LASTEXITCODE -ne 0) { throw 'Sidebar footer layout regression failed' }
