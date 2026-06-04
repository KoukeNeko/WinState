<#
.SYNOPSIS
  Fetches the repository's contributors (and their avatars) into Assets/contributors/
  so they can be embedded into the build as resources.

  Run automatically by CI before publish; can also be run locally to refresh the
  committed snapshot. On any network/API failure it leaves the existing snapshot
  untouched and exits successfully, so a transient hiccup never fails the build.
#>
param([string]$Repo = "KoukeNeko/WinState")

$dir = Join-Path $PSScriptRoot "..\Assets\contributors"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$headers = @{ "User-Agent" = "WinState-build" }
# Authenticated requests (CI passes GITHUB_TOKEN) get a far higher rate limit.
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }

try {
    $contribs = Invoke-RestMethod -Headers $headers `
        -Uri "https://api.github.com/repos/$Repo/contributors?per_page=100"

    $out = @()
    foreach ($c in $contribs) {
        if ($c.type -ne "User") { continue }
        $avatarFile = "$($c.login).png"
        Invoke-WebRequest -Headers $headers -Uri "$($c.avatar_url)&s=144" `
            -OutFile (Join-Path $dir $avatarFile)
        $out += [pscustomobject]@{
            login   = $c.login
            htmlUrl = $c.html_url
            avatar  = $avatarFile
        }
    }

    ($out | ConvertTo-Json -Depth 4 -AsArray) |
        Set-Content -Path (Join-Path $dir "contributors.json") -Encoding UTF8

    Write-Host "Fetched $($out.Count) contributors into Assets/contributors."
}
catch {
    Write-Warning "Could not fetch contributors ($_). Keeping the committed snapshot."
}
