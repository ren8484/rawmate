param(
    [Parameter(Mandatory=$true)][ValidateSet('probe','scan','preflight')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$ExifToolPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$InputPath,
    [string]$SourceFile,
    [string]$TargetFile
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$runnerPath = Join-Path $repoPath 'test-output\GpsRealFileTests.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Split-Path $compilerPath -Parent
& $compilerPath /nologo /target:exe /main:GpsRealFileTests "/out:$runnerPath" (Join-Path $repoPath 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsRealFileTests.cs') "/reference:$frameworkPath\WPF\WindowsBase.dll" "/reference:$frameworkPath\WPF\PresentationCore.dll" "/reference:$frameworkPath\WPF\PresentationFramework.dll" "/reference:$frameworkPath\System.Xaml.dll" /reference:System.Windows.Forms.dll
if ($LASTEXITCODE -ne 0) { throw 'Real-file diagnostic compilation failed' }
& $runnerPath $Mode $ExifToolPath $OutputDirectory $InputPath $SourceFile $TargetFile
if ($LASTEXITCODE -ne 0) { throw 'Real-file diagnostic failed' }
