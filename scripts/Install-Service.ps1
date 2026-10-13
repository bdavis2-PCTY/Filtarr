<#
.SYNOPSIS
    Registers a published Filtarr (see Build-Release.ps1) as a Windows service that starts with the machine.
.DESCRIPTION
    Filtarr hosts its own web server on the port from appsettings.json (Filtarr:Port), like Sonarr/Radarr, so no IIS is needed.
    Run from an elevated PowerShell. Safe to run again: an existing service is reconfigured, not duplicated.
.PARAMETER InstallDir
    Folder holding Filtarr.Api.exe (the published output). The service account needs read access to it.
.PARAMETER DataDirectory
    Where the database and logs live (becomes Filtarr__DataDirectory for this service only). Created if missing and made
    writable for the service account. Keep it outside InstallDir so a redeploy never touches it.
.PARAMETER Port
    Only used to open the Windows Firewall for the port; the app itself reads Filtarr:Port from appsettings.json.
.PARAMETER Account
    Service account. NetworkService is a low-privilege built-in account that needs no password.
.PARAMETER SkipFirewall
    Do not create the inbound firewall rule.
.EXAMPLE
    .\scripts\Install-Service.ps1 -InstallDir C:\Filtarr -DataDirectory C:\ProgramData\Filtarr
#>
param(
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [int]$Port = 5080,
    [string]$Account = 'NT AUTHORITY\NetworkService',
    [string]$ServiceName = 'Filtarr',
    [switch]$SkipFirewall
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated (administrator) PowerShell.' }

$exe = Join-Path (Resolve-Path $InstallDir).Path 'Filtarr.Api.exe'
if (-not (Test-Path $exe)) { throw "Filtarr.Api.exe not found in $InstallDir. Publish first (scripts\Build-Release.ps1)." }

New-Item -ItemType Directory -Force $DataDirectory | Out-Null
icacls $DataDirectory /grant "${Account}:(OI)(CI)M" | Out-Null
icacls (Split-Path $exe) /grant "${Account}:(OI)(CI)RX" | Out-Null

$binPath = "`"$exe`""
if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service $ServiceName -ErrorAction SilentlyContinue
    sc.exe config $ServiceName binPath= $binPath obj= $Account start= auto | Out-Null
}
else {
    sc.exe create $ServiceName binPath= $binPath obj= $Account start= auto DisplayName= 'Filtarr' | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw "sc.exe failed ($LASTEXITCODE)" }

sc.exe description $ServiceName 'Rule-based episode monitoring for Sonarr' | Out-Null
# Restart on crash (5 s, 5 s, 30 s); the counter resets after a day.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

# Per-service environment, so it survives redeploys of the install folder and does not leak into other services.
$envKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
Set-ItemProperty -Path $envKey -Name Environment -Type MultiString -Value @("Filtarr__DataDirectory=$DataDirectory")

if (-not $SkipFirewall) {
    $rule = "Filtarr (TCP $Port)"
    Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

Start-Service $ServiceName
Write-Host "Service '$ServiceName' installed and started. Data: $DataDirectory  URL: http://localhost:$Port/"
