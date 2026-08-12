# WorldSim — 模拟核心 asmdef 边界静态扫描 (V0-1 / G0-1)
# 用法: pwsh tests/ci/check-sim-asmdef.ps1
# 失败 exit 1；可被 CI 与本地预检调用。

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$simRoot = Join-Path $root "WorldSim\Assets\Scripts\Simulation"
if (-not (Test-Path $simRoot)) {
    Write-Error "Simulation root not found: $simRoot"
}

$failures = @()
$csFiles = Get-ChildItem -Path $simRoot -Filter "*.cs" -Recurse
foreach ($f in $csFiles) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    if ($text -match '(?m)^\s*using\s+UnityEngine(\.|;)') {
        $failures += "[using UnityEngine] $($f.FullName)"
    }
    foreach ($sym in @("MonoBehaviour", "GameObject", "UnityEngine.Time", "System.DateTime.Now")) {
        # 粗略词边界：前后非标识符
        $pattern = "(?<![A-Za-z0-9_])$([regex]::Escape($sym))(?![A-Za-z0-9_])"
        if ([regex]::IsMatch($text, $pattern)) {
            $failures += "[$sym] $($f.FullName)"
        }
    }
}

$asmdefs = Get-ChildItem -Path $simRoot -Filter "*.asmdef" -Recurse
foreach ($a in $asmdefs) {
    $json = Get-Content -LiteralPath $a.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($json.noEngineReferences -ne $true) {
        $failures += "[noEngineReferences!=true] $($a.FullName)"
    }
    if ($json.references -contains "UnityEngine" -or ($json.references | Where-Object { $_ -like "UnityEngine.*" })) {
        $failures += "[references UnityEngine*] $($a.FullName)"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL: Simulation asmdef boundary violations:"
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "PASS: WorldSim.Simulation.* 零 UnityEngine.CoreModule / noEngineReferences=true ($($csFiles.Count) cs, $($asmdefs.Count) asmdef)"
exit 0
