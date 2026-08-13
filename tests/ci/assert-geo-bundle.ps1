#Requires -Version 5.1
param([string]$RepoRoot = "")
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
$root = Join-Path $RepoRoot "WorldSim\Assets\StreamingAssets\Geo\v1"
$manifest = Join-Path $root "manifest.txt"
if (-not (Test-Path $manifest)) { throw "Geo manifest missing: $manifest" }
$lines = Get-Content $manifest
$build = ($lines | Where-Object { $_ -like "buildId=*" }) -replace "^buildId=", ""
if ($build -ne "geo-v1-simplified-real-samples-20260813") { throw "Unexpected geo buildId: $build" }
$chunks = $lines | Where-Object { $_ -like "chunk=*" }
if ($chunks.Count -ne 3) { throw "Expected Low/Mid/High chunks, got $($chunks.Count)" }
foreach ($line in $chunks) {
    $parts = ($line -replace "^chunk=", "").Split("|")
    if ($parts.Count -ne 4) { throw "Malformed geo chunk: $line" }
    $path = Join-Path $root $parts[2]
    if (-not (Test-Path $path)) { throw "Geo chunk missing: $path" }
    $file = Get-Item $path
    if ($file.Length -ge 100MB) { throw "Geo chunk exceeds GitHub 100MB: $path" }
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($actual -ne $parts[3].ToLowerInvariant()) { throw "Geo checksum mismatch: $path" }
}
$assets = $lines | Where-Object { $_ -like "asset=*" }
if ($assets.Count -ne 3) { throw "Expected political/probes/NOTICE checksums, got $($assets.Count)" }
foreach ($line in $assets) {
    $parts = ($line -replace "^asset=", "").Split("|")
    if ($parts.Count -ne 2) { throw "Malformed geo asset: $line" }
    $path = Join-Path $root $parts[0]
    if (-not (Test-Path $path)) { throw "Geo asset missing: $path" }
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($actual -ne $parts[1].ToLowerInvariant()) { throw "Geo asset checksum mismatch: $path" }
}
Write-Host "Geo bundle PASS buildId=$build chunks=3 assets=3"
