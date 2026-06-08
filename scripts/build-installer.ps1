#requires -Version 5.1
<#
.SYNOPSIS
    Build the end-user single-file installer for WinState.

.DESCRIPTION
    Three stages, mirroring CI:

      1. Publish WinState (self-contained single-file exe, ~80 MB).
      2. Publish WinState.Installer (WinUI 3 wizard, ~30 MB folder including the WinState
         exe staged into its payload/ subfolder).
      3. Zip the installer folder, embed it into WinState.Bootstrapper, and publish the
         bootstrapper as a single self-contained exe (~110-130 MB).

    The end-user only ever sees the bootstrapper exe; double-clicking it triggers a single
    UAC prompt, silently extracts the wizard to %TEMP%, and launches it.

.PARAMETER Rid
    Runtime identifier: win-x64 (default) or win-arm64.

.PARAMETER Configuration
    Release (default) or Debug.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $root 'WinState.Installer'
$bootstrapRoot = Join-Path $root 'WinState.Bootstrapper'
$payloadDir = Join-Path $installerRoot 'payload'

$winstatePublish = Join-Path $root "artifacts/winstate-$Rid"
$installerPublish = Join-Path $root "artifacts/installer-$Rid"
$bootstrapPublish = Join-Path $root "artifacts/setup-$Rid"

# ---- 1. WinState ------------------------------------------------------------------------------

Write-Host "[1/3] Publishing WinState ($Rid, $Configuration)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'WinState.csproj') `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $winstatePublish
if ($LASTEXITCODE -ne 0) { throw "WinState publish failed" }

# ---- 2. WinState.Installer --------------------------------------------------------------------

Write-Host "[2/3] Staging payload + publishing WinState.Installer" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
Copy-Item -Force (Join-Path $winstatePublish 'WinState.exe') (Join-Path $payloadDir 'WinState.exe')

dotnet publish (Join-Path $installerRoot 'WinState.Installer.csproj') `
    -c $Configuration `
    -r $Rid `
    -o $installerPublish
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

# ---- 3. Bootstrapper --------------------------------------------------------------------------

Write-Host "[3/3] Zipping installer + publishing WinState.Bootstrapper" -ForegroundColor Cyan
$payloadZip = Join-Path $bootstrapRoot 'payload.zip'
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
# Compress-Archive flattens leading-folder cruft if we feed it the dir's contents (with \*).
Compress-Archive -Path (Join-Path $installerPublish '*') -DestinationPath $payloadZip -CompressionLevel Optimal

dotnet publish (Join-Path $bootstrapRoot 'WinState.Bootstrapper.csproj') `
    -c $Configuration `
    -r $Rid `
    -o $bootstrapPublish
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper publish failed" }

# WinState-Setup.exe is the only file that ships to end users.
$setupExe = Get-ChildItem -Path $bootstrapPublish -Filter 'WinState-Setup.exe' | Select-Object -First 1
if ($null -eq $setupExe) { throw "Bootstrapper output missing WinState-Setup.exe" }

Write-Host ""
Write-Host "Single-file installer ready:" -ForegroundColor Green
Write-Host "  $($setupExe.FullName)" -ForegroundColor Green
Write-Host "  ($([math]::Round($setupExe.Length / 1MB, 1)) MB)" -ForegroundColor Gray
