#Requires -Version 5.1
# Local Gate-0: pins + asmdef + full WorldSim.Tests EditMode
param(
    [string]$RepoRoot = "",
    [string]$UnityVersion = "6000.0.81f1"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

& (Join-Path $PSScriptRoot "assert-burst-pinned.ps1") -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "check-sim-asmdef.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "assert-region-presets-synced.ps1") -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "assert-geo-bundle.ps1") -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$unity = & (Join-Path $PSScriptRoot "resolve-unity.ps1") -UnityVersion $UnityVersion -Quiet
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($unity)) {
    Write-Error "Unity resolve failed"
    exit 1
}
Write-Host "Unity exe: $unity"

$proj = Join-Path $RepoRoot "WorldSim"
$out = Join-Path $PSScriptRoot "gate0-local.xml"
$log = Join-Path $PSScriptRoot "gate0-local.log"
if (Test-Path $out) { Remove-Item $out -Force }

$argList = @(
    "-batchmode","-nographics",
    "-projectPath", $proj,
    "-runTests","-testPlatform","EditMode",
    "-assemblyNames","WorldSim.Tests",
    "-testResults", $out,
    "-logFile", $log
)
Write-Host "Launching full WorldSim.Tests EditMode..."
Get-Process Unity -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$p = Start-Process -FilePath $unity -ArgumentList $argList -PassThru -Wait
Write-Host "Unity exit=$($p.ExitCode)"
if (-not (Test-Path $out)) {
    Write-Error "gate0-local.xml missing"
    exit 1
}
[xml]$xml = Get-Content $out
$tr = $xml."test-run"
Write-Host "result=$($tr.result) total=$($tr.total) passed=$($tr.passed) failed=$($tr.failed)"
$failed = [int]$tr.failed + [int]$tr.errors
# Task 6: 新增 6 个真实数据探针 (WorldMapEpic5Tests) + 1 个 Task5 占位替换 (Task5DiscoveryTests)
# + 1 个 buildId 断言 (WorldMapPresentationTests). 真实地理 (build/geo-task4-full) 在 CI 缺失时
# 4 个 RealGeo 测试 Ignore, 故 CI 基线 = 本地 152 - 4 = 148。
# (Task 6 评审修复: 删除与 RealData_KoppenProbesMeetEightyPercentThreshold 重复的
#  FixedBiomeProbes_MeetEightyPercent, 本地 153 -> 152, CI 基线 149 -> 148。)
$min = 178
if ($failed -gt 0 -or [int]$tr.total -lt $min) { exit 1 }
exit 0
