#Requires -Version 5.1
<#
.SYNOPSIS
  Resolve Unity editor exe for a pinned version (Hub path + HKLM Installer registry).

.DESCRIPTION
  Dual confirmation (user-verified for 6000.0.81f1):
    1) C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Unity.exe
    2) HKLM\SOFTWARE\Unity Technologies\Installer\Unity <ver>  -> "Location x64"

  Outputs:
    - Writes UNITY_EXE path to stdout (single line) when -Quiet
    - With -GitHubOutput, appends path=... to $env:GITHUB_OUTPUT
#>
param(
    [string]$UnityVersion = "6000.0.81f1",
    [string]$UnityPathOverride = "",
    [switch]$Quiet,
    [switch]$GitHubOutput
)

$ErrorActionPreference = "Stop"

function Test-UnityExe([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    return (Test-Path -LiteralPath $path) -and ($path -match '(?i)Unity\.exe$')
}

$candidates = New-Object System.Collections.Generic.List[string]
$sources = New-Object System.Collections.Generic.List[string]

function Add-Candidate([string]$path, [string]$source) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    $full = $path.Trim().TrimEnd('\')
    if (-not ($full -match '(?i)Unity\.exe$')) {
        $full = Join-Path $full "Editor\Unity.exe"
    }
    if (-not $candidates.Contains($full)) {
        $candidates.Add($full)
        $sources.Add($source)
    }
}

# 0) explicit override / env
if (-not [string]::IsNullOrWhiteSpace($UnityPathOverride)) {
    Add-Candidate $UnityPathOverride "param -UnityPathOverride"
}
if (-not [string]::IsNullOrWhiteSpace($env:UNITY_PATH)) {
    Add-Candidate $env:UNITY_PATH "env UNITY_PATH"
}
if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR)) {
    Add-Candidate $env:UNITY_EDITOR "env UNITY_EDITOR"
}

# 1) Hub default install location
$hubRoot = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$UnityVersion"
Add-Candidate (Join-Path $hubRoot "Editor\Unity.exe") "Hub ProgramFiles"

$hubRoot86 = Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor\$UnityVersion"
if (Test-Path -LiteralPath $hubRoot86) {
    Add-Candidate (Join-Path $hubRoot86 "Editor\Unity.exe") "Hub ProgramFiles(x86)"
}

# 2) Registry Installer key (dual confirm)
$regPaths = @(
    "HKLM:\SOFTWARE\Unity Technologies\Installer\Unity $UnityVersion",
    "HKLM:\SOFTWARE\WOW6432Node\Unity Technologies\Installer\Unity $UnityVersion"
)
foreach ($rp in $regPaths) {
    if (-not (Test-Path -LiteralPath $rp)) { continue }
    $props = Get-ItemProperty -LiteralPath $rp -ErrorAction SilentlyContinue
    if ($null -eq $props) { continue }
    $loc = $null
    if ($props.PSObject.Properties.Name -contains "Location x64") { $loc = [string]$props."Location x64" }
    elseif ($props.PSObject.Properties.Name -contains "Location") { $loc = [string]$props.Location }
    if (-not [string]::IsNullOrWhiteSpace($loc)) {
        Add-Candidate $loc "registry $rp"
    }
}

$found = $null
$foundSource = $null
for ($i = 0; $i -lt $candidates.Count; $i++) {
    if (Test-UnityExe $candidates[$i]) {
        $found = $candidates[$i]
        $foundSource = $sources[$i]
        break
    }
}

if ($null -eq $found) {
    if (-not $Quiet) {
        Write-Host "ERROR: Unity $UnityVersion not found."
        Write-Host "Tried:"
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            Write-Host ("  [{0}] {1}" -f $sources[$i], $candidates[$i])
        }
        Write-Host "Install Unity $UnityVersion or set UNITY_PATH / use a self-hosted Windows runner with Hub+registry."
    }
    exit 1
}

# Dual-confirm note: Hub path exists AND registry Location matches same root when possible
$hubExe = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
$hubOk = Test-UnityExe $hubExe
$regOk = $false
$regLoc = $null
$regKey = "HKLM:\SOFTWARE\Unity Technologies\Installer\Unity $UnityVersion"
if (Test-Path -LiteralPath $regKey) {
    $p = Get-ItemProperty -LiteralPath $regKey -ErrorAction SilentlyContinue
    if ($p -and ($p.PSObject.Properties.Name -contains "Location x64")) {
        $regLoc = [string]$p."Location x64"
        $regExe = Join-Path $regLoc.TrimEnd('\') "Editor\Unity.exe"
        $regOk = Test-UnityExe $regExe
    }
}

if (-not $Quiet) {
    Write-Host "Resolved Unity: $found"
    Write-Host "Source       : $foundSource"
    Write-Host "Hub confirm  : $hubOk ($hubExe)"
    Write-Host "Reg confirm  : $regOk ($regKey Location x64=$regLoc)"
    if ($hubOk -and $regOk) {
        Write-Host "Dual confirm : PASS (Hub path + registry Installer key)"
    } else {
        Write-Host "Dual confirm : PARTIAL (one source missing; still using resolved exe)"
    }
}

if ($GitHubOutput -and $env:GITHUB_OUTPUT) {
    "path=$found" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "hub_ok=$hubOk" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "reg_ok=$regOk" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

if ($Quiet) { Write-Output $found }
exit 0
