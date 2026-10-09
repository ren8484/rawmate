$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outPath=Join-Path $repoPath ('test-output\gps-ux-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outPath | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$framework=Split-Path $compiler -Parent;$runner=Join-Path $outPath 'GpsUxGridTests.exe'
& $compiler /nologo /target:exe /main:GpsUxGridTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsUxGridTests.cs') (Join-Path $PSScriptRoot 'GpsModelTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll
if($LASTEXITCODE -ne 0){throw 'UX test compile failed'}
& $runner (Join-Path $outPath 'working')
if($LASTEXITCODE -ne 0){throw 'UX/Grid regression failed'}
