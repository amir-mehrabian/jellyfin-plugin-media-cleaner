[CmdletBinding(SupportsShouldProcess)]
param([switch]$Push)

$ErrorActionPreference = "Stop"
$repoPath = Split-Path -Parent $PSScriptRoot
$status = & git -C $repoPath status --porcelain
if ($LASTEXITCODE -ne 0) { throw "Unable to read git status." }
if ($status) { throw "Working tree is not clean." }

$profiles = Get-Content -LiteralPath (Join-Path $PSScriptRoot "jellyfin-profiles.json") -Raw | ConvertFrom-Json
if ($profiles.baseVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid baseVersion: $($profiles.baseVersion)" }
$tag = "v$($profiles.baseVersion)"
$legacyTag = "$tag.0"

foreach ($existingTag in @($tag, $legacyTag)) {
    & git -C $repoPath rev-parse --verify --quiet "refs/tags/$existingTag" *> $null
    if ($LASTEXITCODE -eq 0) { throw "Version $($profiles.baseVersion) is already released as $existingTag." }
}
if (-not (& git -C $repoPath branch --show-current)) { throw "HEAD is detached." }

if ($PSCmdlet.ShouldProcess($repoPath, "Create annotated tag $tag")) {
    & git -C $repoPath tag -a $tag -m "Release $tag"
    if ($LASTEXITCODE -ne 0) { throw "Unable to create tag $tag." }
}
if ($Push -and $PSCmdlet.ShouldProcess("origin", "Push tag $tag")) {
    & git -C $repoPath push origin "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Unable to push tag $tag." }
}
