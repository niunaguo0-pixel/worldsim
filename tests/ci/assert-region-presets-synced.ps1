#Requires -Version 5.1
<#
.SYNOPSIS
  Assert design/gdd/data/region-presets.json ↔ StreamingAssets copy stay byte-identical (P1 drift guard).
#>
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$src = Join-Path $RepoRoot "design\gdd\data\region-presets.json"
$dst = Join-Path $RepoRoot "WorldSim\Assets\StreamingAssets\Data\region-presets.json"

Write-Host "=== assert-region-presets-synced ==="
Write-Host "design : $src"
Write-Host "stream : $dst"

if (-not (Test-Path -LiteralPath $src)) { Write-Error "Missing design presets: $src"; exit 1 }
if (-not (Test-Path -LiteralPath $dst)) { Write-Error "Missing StreamingAssets presets: $dst"; exit 1 }

$h1 = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
$h2 = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
Write-Host "SHA256 design=$h1"
Write-Host "SHA256 stream=$h2"

if ($h1 -ne $h2) {
    Write-Host "ERROR: region-presets.json drifted between design/ and StreamingAssets/."
    Write-Host "Copy design → StreamingAssets (or regenerate both from one source) before merge."
    exit 1
}

Write-Host "=== PASS: region-presets in sync ==="
exit 0
