#requires -Version 5.1
<#
.SYNOPSIS
    Publish WinState, drop the resulting exe into the installer's payload/, then publish the
    installer for the requested RID.

.DESCRIPTION
    Mirrors what CI does so a local `pwsh scripts/build-installer.ps1` produces the same
    artifacts as the GitHub Actions workflow.

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
$payloadDir = Join-Path $installerRoot 'payload'
$winstatePublish = Join-Path $root "artifacts/winstate-$Rid"
$installerPublish = Join-Path $root "artifacts/installer-$Rid"

Write-Host "▶ Publishing WinState ($Rid, $Configuration)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'WinState.csproj') `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $winstatePublish
if ($LASTEXITCODE -ne 0) { throw "WinState publish failed" }

Write-Host "▶ Staging payload" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
Copy-Item -Force (Join-Path $winstatePublish 'WinState.exe') (Join-Path $payloadDir 'WinState.exe')

Write-Host "▶ Publishing WinState.Installer ($Rid, $Configuration)" -ForegroundColor Cyan
dotnet publish (Join-Path $installerRoot 'WinState.Installer.csproj') `
    -c $Configuration `
    -r $Rid `
    -o $installerPublish
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

Write-Host ""
Write-Host "✔ Installer ready: $installerPublish" -ForegroundColor Green
Write-Host "  Distribute the whole folder — WinState.Installer.exe expects payload/WinState.exe sitting next to it." -ForegroundColor Gray
