#Requires -Version 5.1
# V0-8 / B2 / B8: assert Unity editor + com.unity.burst pins (never latest).
param(
    [string]$RepoRoot = "",
    [string]$PinsPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
if ([string]::IsNullOrWhiteSpace($PinsPath)) {
    $PinsPath = Join-Path $PSScriptRoot "version-pins.json"
}

if (-not (Test-Path -LiteralPath $PinsPath)) {
    Write-Error "version-pins.json missing: $PinsPath"
    exit 1
}

$pins = Get-Content -LiteralPath $PinsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedUnity = [string]$pins.unityEditor
$expectedBurst = [string]$pins.burst

Write-Host "=== assert-burst-pinned (V0-8) ==="
Write-Host "RepoRoot      : $RepoRoot"
Write-Host "Expected Unity: $expectedUnity"
Write-Host "Expected Burst: $expectedBurst"

$pv = Join-Path $RepoRoot "WorldSim\ProjectSettings\ProjectVersion.txt"
if (-not (Test-Path -LiteralPath $pv)) {
    Write-Error "ProjectVersion.txt missing: $pv"
    exit 1
}
$pvText = Get-Content -LiteralPath $pv -Raw -Encoding UTF8
$unityPattern = "m_EditorVersion:\s*" + [regex]::Escape($expectedUnity)
if ($pvText -notmatch $unityPattern) {
    Write-Host "ERROR B2: ProjectVersion.txt Unity version mismatch (want $expectedUnity)."
    Write-Host $pvText
    exit 1
}
Write-Host "OK ProjectVersion.txt = $expectedUnity"

$manifest = Join-Path $RepoRoot "WorldSim\Packages\manifest.json"
if (-not (Test-Path -LiteralPath $manifest)) {
    Write-Error "manifest.json missing: $manifest"
    exit 1
}
$manifestText = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
$burstPattern = '"com\.unity\.burst"\s*:\s*"([^"]+)"'
$m = [regex]::Match($manifestText, $burstPattern)
if (-not $m.Success) {
    Write-Host "ERROR B2/B8: manifest.json missing direct com.unity.burst dependency."
    exit 1
}
$actualBurst = $m.Groups[1].Value
if ($actualBurst -ne $expectedBurst) {
    Write-Host "ERROR B2/B8: Burst version $actualBurst != pin $expectedBurst"
    exit 1
}
if (($actualBurst -eq "latest") -or ($expectedBurst -eq "latest")) {
    Write-Host "ERROR B2: latest is forbidden."
    exit 1
}
Write-Host "OK manifest com.unity.burst = $actualBurst (direct pin)"
Write-Host "=== PASS: Unity + Burst pins locked ==="
exit 0
