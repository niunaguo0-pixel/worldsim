# WorldSim — 模拟核心 asmdef 边界静态扫描 (V0-1 / G0-1)
# 用法: pwsh tests/ci/check-sim-asmdef.ps1
# 失败 exit 1；可被 CI 与本地预检调用。

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$simRoot = Join-Path $root "WorldSim\Assets\Scripts\Simulation"
$narrativeRoot = Join-Path $root "WorldSim\Assets\Scripts\Narrative"
if (-not (Test-Path $simRoot)) {
    Write-Error "Simulation root not found: $simRoot"
}

function Test-NoEngineBoundary([string]$scanRoot, [string]$label) {
    $failures = @()
    $csFiles = Get-ChildItem -Path $scanRoot -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $csFiles) {
        $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        if ($text -match '(?m)^\s*using\s+UnityEngine(\.|;)') {
            $failures += "[using UnityEngine] $($f.FullName)"
        }
        foreach ($sym in @("MonoBehaviour", "GameObject", "UnityEngine.Time", "System.DateTime.Now")) {
            $pattern = "(?<![A-Za-z0-9_])$([regex]::Escape($sym))(?![A-Za-z0-9_])"
            if ([regex]::IsMatch($text, $pattern)) {
                $failures += "[$sym] $($f.FullName)"
            }
        }
    }

    $asmdefs = Get-ChildItem -Path $scanRoot -Filter "*.asmdef" -Recurse -ErrorAction SilentlyContinue
    foreach ($a in $asmdefs) {
        $json = Get-Content -LiteralPath $a.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($json.noEngineReferences -ne $true) {
            $failures += "[noEngineReferences!=true] $($a.FullName)"
        }
        if ($json.references -contains "UnityEngine" -or ($json.references | Where-Object { $_ -like "UnityEngine.*" })) {
            $failures += "[references UnityEngine*] $($a.FullName)"
        }
    }

    return [PSCustomObject]@{
        Failures = $failures
        CsCount = @($csFiles).Count
        AsmdefCount = @($asmdefs).Count
        Label = $label
    }
}

$sim = Test-NoEngineBoundary $simRoot "WorldSim.Simulation.*"
$allFailures = @($sim.Failures)

if (Test-Path $narrativeRoot) {
    $nar = Test-NoEngineBoundary $narrativeRoot "WorldSim.Narrative"
    $allFailures += $nar.Failures
    Write-Host "PASS: $($nar.Label) 零 UnityEngine / noEngineReferences=true ($($nar.CsCount) cs, $($nar.AsmdefCount) asmdef)"
}

if ($allFailures.Count -gt 0) {
    Write-Host "FAIL: asmdef boundary violations:"
    $allFailures | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "PASS: $($sim.Label) 零 UnityEngine.CoreModule / noEngineReferences=true ($($sim.CsCount) cs, $($sim.AsmdefCount) asmdef)"
exit 0
