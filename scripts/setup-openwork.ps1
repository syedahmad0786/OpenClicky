[CmdletBinding()]
param(
    [string]$TargetRoot,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$projectRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($TargetRoot)) { $TargetRoot = Join-Path $projectRoot 'artifacts/runtime/openwork' }

$nodeUrl = 'https://nodejs.org/dist/v24.18.1/win-x64/node.exe'
$nodeSha256 = 'ac51903c4c111815d52280b1fdcc8da067cbb37e2fe1a765097b85c3292c8582'
$engineUrl = 'https://github.com/anomalyco/opencode/releases/download/v1.18.30/opencode-windows-x64.zip'
$engineSha256 = 'c8c0e0d05ac3dac544a0edfad8de9eb244bf46c6c7a131c38619d40fcf31bd1f'
$engineExeSha256 = 'c1bdbb18767048e1853af4238311b3f7e16ff2f91b68fb4c7ced3c5175347eea'
$serverIntegrity = '1GRBvtiT0i+PcgBWIe1+7OcfVIFIV7MADaxO39H1DUZ68sjzA53oG9ceDKvPrqKI6xr+4/+vSZ0hvwfM/10vpQ=='
$jsoncIntegrity = 'HUgH65KyejrUFPvHFPbqOY0rsFip3Bo5wb4ngvdi1EpCYWUQDC5V+Y7mZws+DLkr4M//zQJoanu1SP+87Dv1oQ=='
$serverVersion = '0.18.57'
$jsoncVersion = '3.3.1'

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert-File([string]$Path) { if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing required file: $Path" } }
function Assert-Sha256([string]$Path, [string]$Expected) {
    Assert-File $Path
    $actual = Get-Sha256 $Path
    if ($actual -ne $Expected) { throw "SHA-256 mismatch for $Path; got $actual" }
}
function Assert-WithinRoot([string]$Path, [string]$Root) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = ([System.IO.Path]::GetFullPath($Root)).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing path outside engine root: $fullPath" }
}
function Save-VerifiedDownload([string]$Url, [string]$Destination, [string]$Sha256) {
    if (Test-Path -LiteralPath $Destination) { Assert-Sha256 $Destination $Sha256; return }
    if ($VerifyOnly) { throw "Missing verified artifact: $Destination" }
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $temporary = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.download')
    try {
        Invoke-WebRequest -Uri $Url -OutFile $temporary -UseBasicParsing
        Assert-Sha256 $temporary $Sha256
        Move-Item -LiteralPath $temporary -Destination $Destination
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
function Ensure-TrackedFile([string]$Target, [string]$Source) {
    if (Test-Path -LiteralPath $Target) {
        $targetText = (Get-Content -Raw -LiteralPath $Target) -replace "`r`n", "`n"
        $sourceText = (Get-Content -Raw -LiteralPath $Source) -replace "`r`n", "`n"
        if ($targetText -ne $sourceText) { throw "Existing file differs from tracked seed: $Target" }
        return
    }
    if ($VerifyOnly) { throw "Missing tracked seed: $Target" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Target
}

if (-not [System.IO.Path]::IsPathRooted($TargetRoot)) { throw 'TargetRoot must be an absolute path.' }
if ($VerifyOnly) { Assert-File (Join-Path $TargetRoot 'package.json'); Assert-File (Join-Path $TargetRoot 'pnpm-lock.yaml') }
elseif (-not (Test-Path -LiteralPath $TargetRoot)) { New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null }

$engineRoot = Join-Path $TargetRoot 'engine'
$nodePath = Join-Path $TargetRoot 'node.exe'
$engineZip = Join-Path $engineRoot 'opencode-windows-x64.zip'
$engineExe = Join-Path $engineRoot 'opencode.exe'
$lockPath = Join-Path $TargetRoot 'pnpm-lock.yaml'
$manifestPath = Join-Path $TargetRoot 'package.json'
Ensure-TrackedFile $manifestPath (Join-Path $projectRoot 'packaging/openwork/package.json')
Ensure-TrackedFile $lockPath (Join-Path $projectRoot 'packaging/openwork/pnpm-lock.yaml')
Save-VerifiedDownload $nodeUrl $nodePath $nodeSha256
if (-not (Test-Path -LiteralPath $engineRoot)) { if ($VerifyOnly) { throw "Missing engine directory: $engineRoot" }; New-Item -ItemType Directory -Force -Path $engineRoot | Out-Null }
Save-VerifiedDownload $engineUrl $engineZip $engineSha256

if (-not (Test-Path -LiteralPath $engineExe -PathType Leaf)) {
    if ($VerifyOnly) { throw "Missing extracted engine: $engineExe" }
    $temporaryExtract = Join-Path $engineRoot ([guid]::NewGuid().ToString('N') + '.extract')
    Assert-WithinRoot $temporaryExtract $engineRoot
    try {
        Expand-Archive -LiteralPath $engineZip -DestinationPath $temporaryExtract
        $candidate = Get-ChildItem -LiteralPath $temporaryExtract -Recurse -File -Filter 'opencode.exe' | Select-Object -First 1
        if (-not $candidate) { throw 'The verified OpenCode archive did not contain opencode.exe.' }
        Move-Item -LiteralPath $candidate.FullName -Destination $engineExe
    } finally {
        if (Test-Path -LiteralPath $temporaryExtract) { Assert-WithinRoot $temporaryExtract $engineRoot; Remove-Item -LiteralPath $temporaryExtract -Recurse -Force }
    }
}
Assert-Sha256 $engineExe $engineExeSha256

$lock = Get-Content -Raw -LiteralPath $lockPath
if ($lock -notmatch [regex]::Escape("openwork-server@$serverVersion") -or $lock -notmatch [regex]::Escape($serverIntegrity) -or $lock -notmatch [regex]::Escape("jsonc-parser@$jsoncVersion") -or $lock -notmatch [regex]::Escape($jsoncIntegrity)) { throw 'Tracked lockfile does not contain the expected exact package pins.' }
$serverPackage = Join-Path $TargetRoot 'node_modules/.pnpm/openwork-server@0.18.57/node_modules/openwork-server/package.json'
$jsoncPackage = Join-Path $TargetRoot 'node_modules/.pnpm/jsonc-parser@3.3.1/node_modules/jsonc-parser/package.json'
if (-not (Test-Path -LiteralPath $serverPackage) -or -not (Test-Path -LiteralPath $jsoncPackage)) {
    if ($VerifyOnly) { throw 'Pinned OpenWork dependencies are missing.' }
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) { throw 'pnpm is required to bootstrap missing local packages.' }
    & pnpm --dir $TargetRoot install --frozen-lockfile --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw "pnpm install failed with exit code $LASTEXITCODE." }
}
Assert-File $serverPackage; Assert-File $jsoncPackage
$server = Get-Content -Raw -LiteralPath $serverPackage | ConvertFrom-Json
$jsonc = Get-Content -Raw -LiteralPath $jsoncPackage | ConvertFrom-Json
if ($server.name -ne 'openwork-server' -or $server.version -ne $serverVersion -or $server.license -ne 'MIT') { throw 'OpenWork server metadata mismatch.' }
if ($jsonc.name -ne 'jsonc-parser' -or $jsonc.version -ne $jsoncVersion) { throw 'jsonc-parser metadata mismatch.' }

Write-Output "OpenWork runtime verified at $TargetRoot"
Write-Output "Node: $nodeSha256"
Write-Output "OpenCode archive: $engineSha256; executable: $engineExeSha256"
Write-Output "OpenWork server: $serverVersion; jsonc-parser: $jsoncVersion"
Write-Output 'No global install, setup script, account login, or target application was run.'
