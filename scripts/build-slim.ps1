#requires -Version 5.1
<#
.SYNOPSIS
    Build the slim (framework-dependent) single-file WinState.exe.

.DESCRIPTION
    Unlike scripts/build-installer.ps1 (which bundles the .NET 10 runtime into a
    ~80 MB self-contained exe), this produces a slim ~17 MB single-file exe that
    relies on the user already having the .NET 10 Desktop Runtime installed.

    No installer / bootstrapper is produced — the slim exe runs standalone. Use
    this for the "I already have .NET" download alongside the self-contained one.

.PARAMETER Rid
    Runtime identifier: win-x64, win-arm64, or both (default — builds each in turn).

.PARAMETER Configuration
    Release (default) or Debug.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'both')]
    [string]$Rid = 'both',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$rids = if ($Rid -eq 'both') { @('win-x64', 'win-arm64') } else { @($Rid) }

foreach ($r in $rids) {
    $publishDir = Join-Path $root "artifacts/winstate-slim-$r"

    Write-Host "Publishing slim WinState ($r, $Configuration, framework-dependent)" -ForegroundColor Cyan
    dotnet publish (Join-Path $root 'WinState.csproj') `
        -c $Configuration `
        -r $r `
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Slim WinState publish failed for $r" }

    $exe = Join-Path $publishDir 'WinState.exe'
    if (-not (Test-Path $exe)) { throw "Slim publish output missing WinState.exe for $r" }
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

    Write-Host ""
    Write-Host "Slim WinState ready ($r):" -ForegroundColor Green
    Write-Host "  $exe" -ForegroundColor Green
    Write-Host "  ($size MB - requires .NET 10 Desktop Runtime on the target machine)" -ForegroundColor Gray
    Write-Host ""
}
