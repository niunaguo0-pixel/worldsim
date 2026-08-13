#Requires -Version 5.1
# S5 geo bundle gate: lock-derived buildId, manifest checksum, chunk/asset checksums,
# NOTICE presence, single-file < 100MB, red-line rejecting any "simplified" fidelity,
# and a chunk-size regression guard so a future bundle approaching 100MB fails early.
param([string]$RepoRoot = "")
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
$root = Join-Path $RepoRoot "WorldSim\Assets\StreamingAssets\Geo\v1"
$manifest = Join-Path $root "manifest.txt"
if (-not (Test-Path $manifest)) { throw "Geo manifest missing: $manifest" }

# --- 1. Derive expected buildId from sources.lock.json via the geo builder CLI ---
$lockFile = Join-Path $RepoRoot "tools\geo\sources.lock.json"
if (-not (Test-Path $lockFile)) { throw "sources.lock.json missing: $lockFile" }
$buildGeo = Join-Path $RepoRoot "tools\geo\build_geo.py"
$expectedBuild = & python $buildGeo print-build-id
if ($LASTEXITCODE -ne 0) { throw "print-build-id failed (exit $LASTEXITCODE)" }
$expectedBuild = $expectedBuild.Trim()
if ([string]::IsNullOrWhiteSpace($expectedBuild)) { throw "print-build-id returned empty buildId" }

# --- 2. Parse manifest lines ---
$lines = Get-Content $manifest
$kv = @{}
$chunks = @()
$assets = @()
foreach ($raw in $lines) {
    $line = $raw.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith("#")) { continue }
    $eq = $line.IndexOf('=')
    if ($eq -le 0) { continue }
    $key = $line.Substring(0, $eq)
    $value = $line.Substring($eq + 1)
    if ($key -eq "chunk") {
        $parts = $value.Split('|')
        if ($parts.Count -ne 4) { throw "Malformed geo chunk: $line" }
        $chunks += ,@{ Id = $parts[0]; Lod = $parts[1]; Path = $parts[2]; Checksum = $parts[3] }
    } elseif ($key -eq "asset") {
        $parts = $value.Split('|')
        if ($parts.Count -ne 2) { throw "Malformed geo asset: $line" }
        $assets += ,@{ Path = $parts[0]; Checksum = $parts[1] }
    } else {
        $kv[$key] = $value
    }
}

# --- 3. buildId must match the lock-derived value ---
$build = $kv["buildId"]
if ($build -ne $expectedBuild) {
    throw "Geo buildId mismatch: manifest=$build expected(lock)=$expectedBuild"
}

# --- 4. sourcesLockSha256 must be present and consistent with buildId ---
$lockSha = $kv["sourcesLockSha256"]
if ([string]::IsNullOrWhiteSpace($lockSha)) { throw "manifest missing sourcesLockSha256" }
if ($lockSha.Length -ne 64) { throw "sourcesLockSha256 must be 64 hex chars, got $($lockSha.Length)" }
$buildSuffix = $build.Substring("geo-v1-".Length)
if (-not $lockSha.StartsWith($buildSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "buildId suffix '$buildSuffix' must equal first 16 chars of sourcesLockSha256 '$($lockSha.Substring(0,16))'"
}

# --- 5. Red line: fidelity must not contain "simplified" ---
$fidelity = $kv["fidelity"]
if ($fidelity -match "simplified") {
    throw "Geo fidelity must not be simplified (regression): $fidelity"
}

# --- 6. manifestChecksum: recompute over chunk= and asset= lines (canonical order) ---
# Python builder canonicalizes as "chunk|" + "|".join(tuple) joined by LF (no "chunk=" prefix).
$manifestChecksum = $kv["manifestChecksum"]
if ([string]::IsNullOrWhiteSpace($manifestChecksum)) { throw "manifest missing manifestChecksum" }
$canonicalParts = @()
foreach ($c in $chunks) { $canonicalParts += "chunk|$($c.Id)|$($c.Lod)|$($c.Path)|$($c.Checksum)" }
foreach ($a in $assets) { $canonicalParts += "asset|$($a.Path)|$($a.Checksum)" }
$canonical = $canonicalParts -join "`n"
$recomputed = (Get-FileHash -Algorithm SHA256 -InputStream ([System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($canonical)))).Hash.ToLowerInvariant()
if ($recomputed -ne $manifestChecksum.ToLowerInvariant()) {
    throw "manifestChecksum mismatch: manifest=$manifestChecksum recomputed=$recomputed"
}

# --- 7. Chunk count and checksums; single-file < 100MB with regression headroom ---
if ($chunks.Count -ne 3) { throw "Expected Low/Mid/High chunks, got $($chunks.Count)" }
$HARD_LIMIT = 100MB
$REGRESSION_WARN = 80MB
foreach ($c in $chunks) {
    $path = Join-Path $root $c.Path
    if (-not (Test-Path $path)) { throw "Geo chunk missing: $path" }
    $file = Get-Item $path
    if ($file.Length -ge $HARD_LIMIT) {
        throw "Geo chunk exceeds GitHub 100MB hard limit: $path ($($file.Length) bytes)"
    }
    if ($file.Length -ge $REGRESSION_WARN) {
        throw "Geo chunk approaching 100MB (regression guard @80MB): $path ($($file.Length) bytes)"
    }
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($actual -ne $c.Checksum.ToLowerInvariant()) { throw "Geo chunk checksum mismatch: $path" }
}

# --- 8. Asset checksums; political asset must be WSP1 binary, legacy tsv must be gone ---
if ($assets.Count -ne 3) { throw "Expected political/probes/NOTICE assets, got $($assets.Count)" }
$hasPoliticalWsp1 = $false
$hasLegacyTsv = $false
foreach ($a in $assets) {
    $path = Join-Path $root $a.Path
    if (-not (Test-Path $path)) { throw "Geo asset missing: $path" }
    $file = Get-Item $path
    if ($file.Length -ge $HARD_LIMIT) {
        throw "Geo asset exceeds GitHub 100MB hard limit: $path ($($file.Length) bytes)"
    }
    if ($file.Length -ge $REGRESSION_WARN) {
        throw "Geo asset approaching 100MB (regression guard @80MB): $path ($($file.Length) bytes)"
    }
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
    if ($actual -ne $a.Checksum.ToLowerInvariant()) { throw "Geo asset checksum mismatch: $path" }
    if ($a.Path -eq "political-2026.wgeo.gz") { $hasPoliticalWsp1 = $true }
    if ($a.Path -eq "political-2026.tsv") { $hasLegacyTsv = $true }
}
if (-not $hasPoliticalWsp1) { throw "Political asset must be the WSP1 binary political-2026.wgeo.gz" }
if ($hasLegacyTsv) { throw "Legacy political-2026.tsv must be removed from the bundle" }

# --- 9. NOTICE / license presence ---
$noticePath = Join-Path $root "NOTICE.md"
if (-not (Test-Path $noticePath)) { throw "NOTICE.md must ship with the geo bundle" }
$hasNoticeAsset = $false
foreach ($a in $assets) { if ($a.Path -eq "NOTICE.md") { $hasNoticeAsset = $true } }
if (-not $hasNoticeAsset) { throw "NOTICE.md must be listed as a manifest asset" }

# --- 10. Conversion parameters: projection / grids / border year must be declared ---
$requiredConversion = @("projection","pixelConvention","lowGrid","midGrid","highGrid","gzipMtime","borderYear")
$missingConversion = @()
foreach ($name in $requiredConversion) {
    if (-not $kv.ContainsKey("conversion.$name")) { $missingConversion += $name }
}
if ($missingConversion.Count -gt 0) {
    throw "manifest missing conversion parameters: $($missingConversion -join ', ')"
}
if ($kv["conversion.projection"] -ne "equirectangular") {
    throw "conversion.projection must be equirectangular, got $($kv['conversion.projection'])"
}
if ($kv["conversion.highGrid"] -ne "720x360") { throw "conversion.highGrid must be 720x360, got $($kv['conversion.highGrid'])" }
if ($kv["conversion.borderYear"] -ne "2026") { throw "conversion.borderYear must be 2026, got $($kv['conversion.borderYear'])" }

Write-Host "Geo bundle PASS buildId=$build chunks=$($chunks.Count) assets=$($assets.Count) fidelity=$fidelity"
