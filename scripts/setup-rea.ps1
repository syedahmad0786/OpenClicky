[CmdletBinding()]
param(
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/morluto/rea.git'
$commit = 'edf46248f2a44b4e5776518489fee09d0fc4516b'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'artifacts/tools/rea'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required.'
}

if (-not (Test-Path (Join-Path $target '.git'))) {
    if ($VerifyOnly) { throw "Pinned REA checkout is missing: $target" }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    git clone --no-checkout $repoUrl $target
}

$actualRemote = (git -C $target remote get-url origin).Trim()
if ($actualRemote -ne $repoUrl) {
    throw "Refusing checkout with unexpected origin: $actualRemote"
}

$dirty = git -C $target status --porcelain
if ($dirty) {
    throw 'Refusing to change a dirty REA checkout.'
}

$actual = (git -C $target rev-parse HEAD).Trim()
if ($actual -ne $commit) {
    if ($VerifyOnly) { throw "REA checkout is $actual; expected $commit" }
    git -C $target fetch --depth 1 origin $commit
    git -C $target checkout --detach $commit
}

$actual = (git -C $target rev-parse HEAD).Trim()
if ($actual -ne $commit) {
    throw "REA checkout did not reach the pinned commit: $actual"
}

Write-Output "REA checkout verified: $actual"
Write-Output 'No npm install, npx setup, installer, target application, or MCP server was run.'
