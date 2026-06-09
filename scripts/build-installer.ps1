#requires -Version 5.1
<#
.SYNOPSIS
    Build the end-user single-file installer for WinState.

.DESCRIPTION
    Three stages, mirroring CI:

      1. Publish WinState (single-file exe).
      2. Publish WinState.Installer (WinUI 3 wizard folder including the WinState exe staged
         into its payload/ subfolder).
      3. Zip the installer folder, embed it into WinState.Bootstrapper, and publish the
         bootstrapper as a single exe.

    The end-user only ever sees the bootstrapper exe; double-clicking it triggers a single
    UAC prompt, silently extracts the wizard to %TEMP%, and launches it.

    By default every stage is self-contained (bundles the .NET 10 runtime; the setup exe is
    ~110-130 MB and needs no .NET install on the target). With -Slim the .NET runtime is left
    out of all three (framework-dependent): the setup exe is far smaller but the target machine
    must have the .NET 10 Desktop Runtime installed. The Windows App SDK stays bundled either
    way, so the wizard still runs without a separate Windows App Runtime install.

.PARAMETER Rid
    Runtime identifier: win-x64 (default) or win-arm64.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Slim
    Build the framework-dependent (slim) variant: leaves the .NET 10 runtime out of WinState,
    the installer and the bootstrapper. Outputs to artifacts/*-$Rid-slim so it never clobbers
    the self-contained build.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Slim
)

$ErrorActionPreference = 'Stop'

# Slim => framework-dependent .NET across all three projects; otherwise self-contained.
$selfContained = if ($Slim) { 'false' } else { 'true' }
$suffix = if ($Slim) { '-slim' } else { '' }
$variant = if ($Slim) { 'slim, framework-dependent' } else { 'self-contained' }

$root = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $root 'WinState.Installer'
$bootstrapRoot = Join-Path $root 'WinState.Bootstrapper'
$payloadDir = Join-Path $installerRoot 'payload'

$winstatePublish = Join-Path $root "artifacts/winstate-$Rid$suffix"
$installerPublish = Join-Path $root "artifacts/installer-$Rid$suffix"
$bootstrapPublish = Join-Path $root "artifacts/setup-$Rid$suffix"

# ---- 1. WinState ------------------------------------------------------------------------------

Write-Host "[1/3] Publishing WinState ($Rid, $Configuration, $variant)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'WinState.csproj') `
    -c $Configuration `
    -r $Rid `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $winstatePublish
if ($LASTEXITCODE -ne 0) { throw "WinState publish failed" }

# ---- 2. WinState.Installer --------------------------------------------------------------------

Write-Host "[2/3] Staging payload + publishing WinState.Installer" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
Copy-Item -Force (Join-Path $winstatePublish 'WinState.exe') (Join-Path $payloadDir 'WinState.exe')

# SelfContained toggles the .NET runtime only; WindowsAppSDKSelfContained stays true (set in
# the csproj) so the WinUI 3 wizard always carries its own Windows App SDK.
dotnet publish (Join-Path $installerRoot 'WinState.Installer.csproj') `
    -c $Configuration `
    -r $Rid `
    -p:SelfContained=$selfContained `
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
    -p:SelfContained=$selfContained `
    -o $bootstrapPublish
if ($LASTEXITCODE -ne 0) { throw "Bootstrapper publish failed" }

# WinState-Setup.exe is the only file that ships to end users.
$setupExe = Get-ChildItem -Path $bootstrapPublish -Filter 'WinState-Setup.exe' | Select-Object -First 1
if ($null -eq $setupExe) { throw "Bootstrapper output missing WinState-Setup.exe" }

Write-Host ""
Write-Host "Single-file installer ready ($variant):" -ForegroundColor Green
Write-Host "  $($setupExe.FullName)" -ForegroundColor Green
Write-Host "  ($([math]::Round($setupExe.Length / 1MB, 1)) MB)" -ForegroundColor Gray
