param([Parameter(Mandatory=$true)][string]$BaselineRoot,[Parameter(Mandatory=$true)][string]$ManifestSha256)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($BaselineRoot).TrimEnd('\')
if(-not $root.StartsWith('C:\rawmate\test-output\',[StringComparison]::OrdinalIgnoreCase)){throw 'Baseline outside test-output'}
$manifest=Join-Path $root 'release-baseline-1.5-manifest.json'
if((Get-FileHash -LiteralPath $manifest).Hash -ne $ManifestSha256){throw 'Release manifest identity changed'}
$data=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if($data.fixtures.Count -ne 24){throw 'Expected 24 frozen inputs including explicit pairs'}
foreach($f in $data.fixtures){
 $p=[IO.Path]::GetFullPath((Join-Path $root $f.relativePath))
 if(-not $p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Baseline escaped root'}
 $item=Get-Item -LiteralPath $p
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Baseline link rejected'}
 if($item.Length -ne $f.size -or (Get-FileHash -LiteralPath $p).Hash -ne $f.sha256){throw "Frozen fixture changed: $($f.relativePath)"}
}
Write-Output "PASS current release baseline: $($data.fixtures.Count)/$($data.fixtures.Count) sizes + SHA256; manifest $ManifestSha256"
