param([Parameter(Mandatory=$true)][ValidateSet('readonly','evidence','transactions')][string]$Mode,[Parameter(Mandatory=$true)][string]$PairsPath,[Parameter(Mandatory=$true)][string]$ExifToolPath,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outputFull=[IO.Path]::GetFullPath($OutputPath)
if(-not $outputFull.StartsWith(($repoPath+'\test-output\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay in test-output'}
if(Test-Path -LiteralPath $outputFull){throw 'Do not overwrite prior evidence or working copies'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler -Parent
$runner=Join-Path (Split-Path $OutputPath -Parent) 'CreateGpsIfdTests.exe'
& $compiler /nologo /optimize+ /target:exe /main:CreateGpsIfdTests "/out:$runner" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'CreateGpsIfdTests.cs') "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" "/r:$framework\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll
if($LASTEXITCODE -ne 0){throw 'CreateGpsIfd compilation failed'}
& $runner $Mode $PairsPath $ExifToolPath $OutputPath
if($LASTEXITCODE -ne 0){throw 'CreateGpsIfd production safety test failed; evidence retained'}
