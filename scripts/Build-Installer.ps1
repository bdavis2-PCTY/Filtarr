<#
.SYNOPSIS
    Builds the Filtarr Windows installer (installer\output\Filtarr-Setup-<version>.exe).
.DESCRIPTION
    1. Runs Build-Release.ps1 for a self-contained win-x64 release (no .NET install needed on the target machine)
       into installer\staging (cleared first; git-ignored).
    2. Checks the staged appsettings.json still has the lines the installer rewrites.
    3. Compiles installer\Filtarr.iss with Inno Setup 6 (https://jrsoftware.org/isinfo.php; winget install JRSoftware.InnoSetup).
.PARAMETER Version
    Installer/product version. Defaults to today's date as yyyy.M.d.
.PARAMETER IsccPath
    Path to ISCC.exe if Inno Setup is not in a standard location or on the PATH.
.PARAMETER SkipTests
    Passed through to Build-Release.ps1.
.EXAMPLE
    .\scripts\Build-Installer.ps1 -Version 1.0.0
#>
param(
    [string]$Version,
    [string]$IsccPath,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

# (Computed here, not as a param() default: $PSScriptRoot is empty in param defaults on Windows PowerShell 5.1.)
$Root = Split-Path -Parent $PSScriptRoot
$Staging = Join-Path $Root 'installer\staging'
$Output = Join-Path $Root 'installer\output'
$Script = Join-Path $Root 'installer\Filtarr.iss'
if (-not $Version) { $Version = (Get-Date).ToString('yyyy.M.d') }

if (-not $IsccPath) {
    $candidates = @()
    foreach ($ver in @('7', '6')) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup $ver\ISCC.exe")
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup $ver\ISCC.exe")
        $candidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup $ver\ISCC.exe")
    }
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $onPath = Get-Command iscc -ErrorAction SilentlyContinue
        if ($onPath) { $IsccPath = $onPath.Source }
    }
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw 'Inno Setup 6 or 7 (ISCC.exe) was not found. Install it (winget install JRSoftware.InnoSetup) or pass -IsccPath.'
}

Write-Host "==> Building the self-contained release ($Version)"
$releaseArgs = @{ OutputDir = $Staging; SelfContained = $true; Runtime = 'win-x64' }
if ($SkipTests) { $releaseArgs.SkipTests = $true }
& (Join-Path $PSScriptRoot 'Build-Release.ps1') @releaseArgs
if (-not (Test-Path (Join-Path $Staging 'Filtarr.Api.exe'))) { throw 'The release build did not produce Filtarr.Api.exe.' }

Write-Host '==> Checking appsettings.json'
$settings = Get-Content (Join-Path $Staging 'appsettings.json') -Raw
foreach ($line in @('"Port": 5080', '"DataDirectory": "%LOCALAPPDATA%\\Filtarr"')) {
    if (-not $settings.Contains($line)) { throw "appsettings.json no longer contains $line; update PortLine/DataDirLine in installer\Filtarr.iss." }
}

Write-Host '==> Compiling the installer'
# ISCC writes warnings to stderr; on Windows PowerShell 5.1 that would abort the script, so judge success by the exit code only.
$ErrorActionPreference = 'Continue'
& $IsccPath "/DAppVersion=$Version" "/DSourceDir=$Staging" "/DOutputDir=$Output" $Script
$isccExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($isccExit -ne 0) { throw "ISCC failed (exit code $isccExit)." }

$setup = Join-Path $Output "Filtarr-Setup-$Version.exe"
Write-Host ("Installer ready: {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
