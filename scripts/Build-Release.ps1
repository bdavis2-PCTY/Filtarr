<#
.SYNOPSIS
    Builds a Filtarr release (API + UI) into a single folder.

.DESCRIPTION
    1. Runs the backend tests (skip with -SkipTests).
    2. Builds the React UI (npm ci when node_modules is missing or out of date, then npm run build) into src\Filtarr.Api\wwwroot.
    3. Publishes the API in Release configuration into the output folder; the published API serves the UI.
    4. Removes any appsettings.*.json except appsettings.json (they can contain real keys), verifies the result,
       and writes release-info.json (build time, git commit, options).

    The output folder is cleaned first. Everything in it is regenerated on every run.

    The result runs with:   dotnet Filtarr.Api.dll      (or Filtarr.Api.exe)
    A framework-dependent release (the default) needs the ASP.NET Core 10 runtime on the machine that runs it;
    use -SelfContained to bundle the runtime instead.

.PARAMETER OutputDir
    Where the release is written. Default: <repo>\output  (C:\Paylocity\Filtarr\output when the repo is at C:\Paylocity\Filtarr).

.PARAMETER Configuration
    Build configuration. Default: Release. (The app picks its environment from this: Release runs as Production.)

.PARAMETER Runtime
    Optional runtime identifier, e.g. win-x64 or linux-x64, to publish for a specific platform.

.PARAMETER SelfContained
    Bundle the .NET runtime (larger output, no runtime needed on the target). Implies -Runtime win-x64 if none is given.

.PARAMETER SkipTests
    Do not run the backend tests first.

.PARAMETER SkipInstall
    Never run "npm ci" (use the existing frontend\node_modules as it is).

.PARAMETER CleanInstall
    Always run "npm ci", even if frontend\node_modules already matches package-lock.json. By default the install is skipped
    in that case (it also fails while "npm run dev" is running, because that holds files in node_modules open).

.PARAMETER SmokeTest
    After publishing, start the release on a free port with a throw-away data folder and check that the API and UI respond.

.EXAMPLE
    .\scripts\Build-Release.ps1

.EXAMPLE
    .\scripts\Build-Release.ps1 -SelfContained -Runtime win-x64 -SmokeTest

.NOTES
    If scripts are blocked on your machine:  powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$Configuration = 'Release',
    [string]$Runtime,
    [switch]$SelfContained,
    [switch]$SkipTests,
    [switch]$SkipInstall,
    [switch]$CleanInstall,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$Solution = Join-Path $RepoRoot 'Filtarr.slnx'
$ApiProject = Join-Path $RepoRoot 'src\Filtarr.Api\Filtarr.Api.csproj'
$FrontendDir = Join-Path $RepoRoot 'frontend'
$WwwRoot = Join-Path $RepoRoot 'src\Filtarr.Api\wwwroot'
# (Computed here, not as a param() default: $PSScriptRoot is empty in param defaults on Windows PowerShell 5.1.)
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'output' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir).TrimEnd('\')

function Write-Step([string]$Title) {
    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
}

# Runs a native command (given as a script block) and stops the script if it fails.
function Invoke-Native([string]$What, [scriptblock]$Command) {
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

function Assert-Tool([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "'$Name' was not found on PATH. $Hint" }
}

# The output folder is emptied on every run, so refuse anything that could be (or contain) source code.
function Assert-SafeOutputDir {
    $root = [IO.Path]::GetPathRoot($OutputDir).TrimEnd('\')
    if ($OutputDir -eq $root) { throw "Refusing to use a drive root as the output folder: $OutputDir" }
    if ($RepoRoot -eq $OutputDir -or $RepoRoot.StartsWith($OutputDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "The output folder must not be the repository folder or one of its parents: $OutputDir"
    }
    foreach ($name in 'src', 'frontend', 'scripts', '.git', '.github', 'etc') {
        $protected = Join-Path $RepoRoot $name
        if ($OutputDir -eq $protected -or $OutputDir.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "The output folder must not be inside '$name': $OutputDir"
        }
    }
}

# True when every non-optional package in package-lock.json is installed in frontend\node_modules at the locked version
# (npm records what it installed in node_modules\.package-lock.json).
function Test-NodeModulesMatchLockfile {
    $installedPath = Join-Path $FrontendDir 'node_modules\.package-lock.json'
    $lockPath = Join-Path $FrontendDir 'package-lock.json'
    if (-not ((Test-Path -LiteralPath $installedPath) -and (Test-Path -LiteralPath $lockPath))) { return $false }
    try {
        # npm's lock files contain a package whose name is the empty string (the project itself), which Windows PowerShell 5.1's
        # ConvertFrom-Json rejects; give that one key a name first.
        $emptyKey = '(?m)^(\s*)"":(\s*\{)'
        $lock = (Get-Content -LiteralPath $lockPath -Raw) -replace $emptyKey, '$1"_root":$2' | ConvertFrom-Json
        $installed = (Get-Content -LiteralPath $installedPath -Raw) -replace $emptyKey, '$1"_root":$2' | ConvertFrom-Json
        $versions = @{}
        foreach ($p in $installed.packages.PSObject.Properties) { $versions[$p.Name] = $p.Value.version }
        foreach ($p in $lock.packages.PSObject.Properties) {
            if ($p.Name -eq '_root') { continue }
            $optional = ($p.Value.PSObject.Properties.Name -contains 'optional') -and $p.Value.optional
            if (-not $versions.ContainsKey($p.Name)) {
                if ($optional) { continue }   # e.g. native binaries for other platforms
                return $false
            }
            if ($versions[$p.Name] -ne $p.Value.version) { return $false }
        }
        return $true
    }
    catch { return $false }
}

$timer = [Diagnostics.Stopwatch]::StartNew()

Write-Step 'Checking prerequisites'
Assert-Tool 'dotnet' 'Install the .NET 10 SDK.'
Assert-Tool 'node' 'Install Node.js 20.19+ or 22+.'
Assert-Tool 'npm' 'Install Node.js (it includes npm).'
foreach ($path in $Solution, $ApiProject, (Join-Path $FrontendDir 'package.json')) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Expected file not found: $path (is the script inside the repository's scripts folder?)" }
}
Assert-SafeOutputDir
if ($SelfContained -and -not $Runtime) { $Runtime = 'win-x64' }
Write-Host "Repository : $RepoRoot"
Write-Host "Output     : $OutputDir"
Write-Host ("Build      : {0}{1}{2}" -f $Configuration, $(if ($Runtime) { ", runtime $Runtime" } else { '' }), $(if ($SelfContained) { ', self-contained' } else { '' }))

if (-not $SkipTests) {
    Write-Step 'Running backend tests'
    Invoke-Native 'dotnet test' { dotnet test $Solution --configuration $Configuration --nologo --verbosity minimal }
}

Write-Step 'Building the UI'
Push-Location $FrontendDir
try {
    if ($SkipInstall) {
        Write-Host 'Skipping npm ci (-SkipInstall).'
    }
    elseif (-not $CleanInstall -and (Test-NodeModulesMatchLockfile)) {
        # Reinstalling would also fail while a dev server (npm run dev) has files in node_modules open.
        Write-Host 'node_modules already matches package-lock.json; skipping npm ci (use -CleanInstall to force it).'
    }
    else {
        # "npm ci" deletes node_modules before reinstalling. If something is running out of it (e.g. "npm run dev"), Windows
        # keeps some files locked, so the delete stops half-way and leaves node_modules broken. Never start that.
        $inUse = @(Get-CimInstance Win32_Process | Where-Object {
                ($_.ExecutablePath -and $_.ExecutablePath.StartsWith("$FrontendDir\node_modules\", [StringComparison]::OrdinalIgnoreCase)) -or
                ($_.CommandLine -and $_.CommandLine.IndexOf("$FrontendDir\node_modules\", [StringComparison]::OrdinalIgnoreCase) -ge 0)
            })
        if ($inUse.Count -gt 0) {
            throw ("Cannot reinstall frontend packages: $($inUse.Count) running process(es) are using frontend\node_modules " +
                "(e.g. 'npm run dev' / Vite: PID $($inUse[0].ProcessId)). Nothing was changed. Stop them and run again, " +
                "or use -SkipInstall to build with the packages that are already installed.")
        }
        try { Invoke-Native 'npm ci' { npm ci --no-audit --no-fund } }
        catch {
            throw ("$($_.Exception.Message) If npm reported EPERM or EBUSY, something is using frontend\node_modules " +
                "(a running 'npm run dev', an editor or antivirus): stop it and run again, or use -SkipInstall.")
        }
    }
    Invoke-Native 'UI build' { npm run build }
}
finally { Pop-Location }
if (-not (Test-Path -LiteralPath (Join-Path $WwwRoot 'index.html'))) { throw "The UI build did not produce $WwwRoot\index.html." }

Write-Step 'Cleaning the output folder'
if (Test-Path -LiteralPath $OutputDir) {
    Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Step 'Publishing the API (with the UI)'
$publishArgs = @('publish', $ApiProject, '--configuration', $Configuration, '--output', $OutputDir, '--nologo')
if ($Runtime) { $publishArgs += @('--runtime', $Runtime) }
$publishArgs += @('--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }))
Invoke-Native 'dotnet publish' { dotnet @publishArgs }

Write-Step 'Verifying the release'
# Environment-specific settings files can hold real keys and must never ship. (The project already excludes them from
# publish; this is a second line of defence.)
foreach ($file in Get-ChildItem -LiteralPath $OutputDir -Filter 'appsettings.*.json' -File -Force) {
    Remove-Item -LiteralPath $file.FullName -Force
    Write-Host "Removed $($file.Name) from the release (may contain secrets)." -ForegroundColor Yellow
}
$required = @('Filtarr.Api.dll', 'appsettings.json', 'wwwroot\index.html')
$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $OutputDir $_)) }
if ($missing) { throw "The release is incomplete; missing: $($missing -join ', ')" }
if (-not (Get-ChildItem -LiteralPath (Join-Path $OutputDir 'wwwroot\assets') -Filter '*.js' -ErrorAction SilentlyContinue)) {
    throw 'The release is incomplete: the UI bundle (wwwroot\assets\*.js) is missing.'
}
Write-Host 'API and UI are both present.'

# release-info.json: what was built, from which commit.
$commit = $null; $branch = $null; $dirty = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    Push-Location $RepoRoot
    try {
        $commit = (git rev-parse --short HEAD 2>$null)
        $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
        $dirty = [bool](git status --porcelain 2>$null)
    }
    finally { Pop-Location }
}
$info = [ordered]@{
    builtAtUtc    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    configuration = $Configuration
    runtime       = $(if ($Runtime) { $Runtime } else { $null })
    selfContained = [bool]$SelfContained
    gitCommit     = $commit
    gitBranch     = $branch
    uncommittedChanges = $dirty
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $OutputDir 'release-info.json'), ($info | ConvertTo-Json), $utf8NoBom)
Copy-Item -LiteralPath (Join-Path $RepoRoot 'README.md') -Destination (Join-Path $OutputDir 'README.md') -Force

if ($SmokeTest) {
    Write-Step 'Smoke test'
    $listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $dataDir = Join-Path ([IO.Path]::GetTempPath()) ('filtarr-smoke-' + [Guid]::NewGuid().ToString('N'))
    $saved = @{ Port = $env:Filtarr__Port; Data = $env:Filtarr__DataDirectory }
    $env:Filtarr__Port = "$port"
    $env:Filtarr__DataDirectory = $dataDir
    $process = $null
    try {
        $process = Start-Process -FilePath 'dotnet' -ArgumentList @('Filtarr.Api.dll') -WorkingDirectory $OutputDir -PassThru -WindowStyle Hidden
        $base = "http://localhost:$port"
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            if ($process.HasExited) { throw "The release exited during start-up (exit code $($process.ExitCode))." }
            try { $ready = (Invoke-WebRequest -Uri "$base/api/fields" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { throw 'The API did not answer within 30 seconds.' }
        $page = Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 5
        if ($page.Content -notmatch 'id="root"') { throw 'The API answered, but it is not serving the UI.' }
        Write-Host "API and UI respond on $base." -ForegroundColor Green
    }
    finally {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $env:Filtarr__Port = $saved.Port
        $env:Filtarr__DataDirectory = $saved.Data
        if (Test-Path -LiteralPath $dataDir) { Start-Sleep -Milliseconds 300; Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

$timer.Stop()
$size = (Get-ChildItem -LiteralPath $OutputDir -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host ("Release ready: {0}  ({1:N1} MB, {2:N0}s)" -f $OutputDir, ($size / 1MB), $timer.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host "Run it with:  cd `"$OutputDir`"; dotnet Filtarr.Api.dll"
