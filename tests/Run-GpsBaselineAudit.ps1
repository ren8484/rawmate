param(
    [Parameter(Mandatory=$true)][string]$ExifToolPath,
    [Parameter(Mandatory=$true)][string]$InputRoot,
    [Parameter(Mandatory=$true)][string]$BaselineDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [switch]$ReuseVerboseTranscripts
)
$ErrorActionPreference = 'Stop'
$auditRepo = Split-Path $PSScriptRoot -Parent
$auditOutput = [IO.Path]::GetFullPath($OutputDirectory)
$auditAllowed = [IO.Path]::GetFullPath((Join-Path $auditRepo 'test-output')) + '\'
if (-not $auditOutput.StartsWith($auditAllowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Audit output must be inside repository test-output, never inside photo root' }
if (-not $ReuseVerboseTranscripts -and (Test-Path -LiteralPath $auditOutput)) { throw 'Use a new output directory to preserve existing evidence' }
$auditCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$auditFramework = Split-Path $auditCompiler -Parent
$auditRunner = Join-Path $auditRepo 'test-output\GpsBaselineAudit.exe'
& $auditCompiler /nologo /target:exe /main:GpsBaselineAudit "/out:$auditRunner" (Join-Path $auditRepo 'RAWMate.cs') (Join-Path $PSScriptRoot 'GpsBaselineAudit.cs') "/reference:$auditFramework\WPF\WindowsBase.dll" "/reference:$auditFramework\WPF\PresentationCore.dll" "/reference:$auditFramework\WPF\PresentationFramework.dll" "/reference:$auditFramework\System.Xaml.dll" /reference:System.Windows.Forms.dll
if ($LASTEXITCODE -ne 0) { throw 'Audit compilation failed' }
if ($ReuseVerboseTranscripts) { & $auditRunner schemas $auditOutput }
else { & $auditRunner $ExifToolPath $InputRoot $auditOutput $BaselineDirectory }
if ($LASTEXITCODE -ne 0) { throw 'Read-only audit failed' }
