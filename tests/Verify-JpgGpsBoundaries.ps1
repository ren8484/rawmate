param(
 [Parameter(Mandatory=$true)][string]$InventoryPath,
 [Parameter(Mandatory=$true)][string]$HistoricalRoot,
 [Parameter(Mandatory=$true)][string]$CurrentRoot,
 [Parameter(Mandatory=$true)][string]$FormalSha256,
 [Parameter(Mandatory=$true)][string]$MainCommit,
 [Parameter(Mandatory=$true)][string]$ToolRoot,
 [Parameter(Mandatory=$true)][string]$OutputPath,
 [string[]]$FallbackRoots=@()
)
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$baseline=Import-Csv -LiteralPath $InventoryPath
if($baseline.Count -ne 226){throw 'Unexpected baseline inventory'}
$outputFull=[IO.Path]::GetFullPath($OutputPath)
if(-not $outputFull.StartsWith(($repoPath+'\test-output\'),[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputFull)){throw 'Use fresh test-output evidence'}
$prefix=[IO.Path]::GetFullPath($HistoricalRoot).TrimEnd('\')+'\'
$rows=[Collections.Generic.List[object]]::new()
$fallbackFiles=@(foreach($scope in $FallbackRoots){Get-ChildItem -LiteralPath $scope -File -Recurse | Where-Object {$_.Extension -match '^\.(jpg|jpeg|arw)$'}})
$hashCache=@{}
foreach($photo in $baseline){
 if(-not $photo.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory path outside explicit historical root'}
 $relative=$photo.Path.Substring($prefix.Length)
 $current=[IO.Path]::GetFullPath((Join-Path $CurrentRoot $relative))
 $currentPrefix=[IO.Path]::GetFullPath($CurrentRoot).TrimEnd('\')+'\'
 if(-not $current.StartsWith($currentPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Mapped path escaped current root'}
 $matches=[Collections.Generic.List[string]]::new()
 $mappedSHA=$null
 if(Test-Path -LiteralPath $current){$mappedSHA=(Get-FileHash -LiteralPath $current).Hash;if($mappedSHA -eq $photo.SHA256){$matches.Add($current)}}
 if($matches.Count -eq 0){
  # Explicit scope and historical size/hash, never filename-only matching.
  foreach($candidate in ($fallbackFiles | Where-Object {$_.Length -eq [long]$photo.Bytes})){
   if(-not $hashCache.ContainsKey($candidate.FullName)){$hashCache[$candidate.FullName]=(Get-FileHash -LiteralPath $candidate.FullName).Hash}
   if($hashCache[$candidate.FullName] -eq $photo.SHA256){$matches.Add($candidate.FullName)}
  }
 }
 $rows.Add([PSCustomObject]@{historicPath=$photo.Path;historicalSHA=$photo.SHA256;relativePath=$relative;mappedPath=$current;mappedSHA=$mappedSHA;matches=@($matches);matched=($matches.Count -gt 0)})
}
$rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputFull -Encoding utf8
$found=@($rows | Where-Object matched).Count
Write-Output "Historical 226 baseline: $found/226 exact SHA matches; evidence=$outputFull"
if($found -ne 226){throw '226 gate 尚未恢复; no filename-only replacement or four-file substitution allowed'}
$formal=(Get-FileHash -LiteralPath (Join-Path $repoPath 'RAWMate.exe')).Hash
if($formal -ne $FormalSha256){throw 'Formal EXE changed'}
if((& git -C $repoPath rev-parse main) -ne $MainCommit){throw 'main changed'}
$toolRoot=[IO.Path]::GetFullPath($ToolRoot)
$toolFiles=[string[]](Get-ChildItem -LiteralPath $toolRoot -File -Recurse | ForEach-Object FullName)
[Array]::Sort($toolFiles,[StringComparer]::Ordinal)
$lines=[Collections.Generic.List[string]]::new();$lines.Add("RelativePath`tBytes`tSHA256")
foreach($file in $toolFiles){$relative=$file.Substring($toolRoot.Length+1).Replace('\','/');$lines.Add($relative+"`t"+(Get-Item -LiteralPath $file).Length+"`t"+(Get-FileHash -LiteralPath $file).Hash)}
$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($lines -join "`n")+"`n")
$sha=[Security.Cryptography.SHA256]::Create();try{$identity=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
if($identity -ne '94C298C2C60460507FFA7F3098F26B69E56490A08269AFB2AFE639F84048E750'){throw 'Tool identity changed'}
Write-Output "PASS all 226 baseline files unchanged; formal EXE/main unchanged; full $($toolFiles.Count)-file tool identity $identity"
