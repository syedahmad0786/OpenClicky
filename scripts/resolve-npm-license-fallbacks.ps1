[CmdletBinding()]
param(
    [string] $WorkspaceRoot,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $WorkspaceRoot) { $WorkspaceRoot = Join-Path $projectRoot 'apps/companion-workspace' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'packaging/third-party-license-fallbacks/npm' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($output) -ne 'npm') { throw 'OutputDirectory must end in npm.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$raw = & pnpm --dir $WorkspaceRoot licenses list --prod --json | Out-String
if ($LASTEXITCODE -ne 0) { throw "pnpm license inventory failed with exit code $LASTEXITCODE." }
$groups = $raw | ConvertFrom-Json
$packages = @{}
foreach ($group in $groups.PSObject.Properties) {
    foreach ($summary in $group.Value) {
        foreach ($path in $summary.paths) {
            $metadata = Get-Content -Raw -LiteralPath (Join-Path $path 'package.json') | ConvertFrom-Json
            $key = "$($metadata.name)@$($metadata.version)"
            if ($packages.ContainsKey($key)) { continue }
            $hasText = @(Get-ChildItem -LiteralPath $path -File -ErrorAction SilentlyContinue | Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|COPYING|COPYRIGHT)(\.|$)').Count -gt 0
            $packages[$key] = [ordered]@{ name=$metadata.name; version=$metadata.version; hasText=$hasText }
        }
    }
}

$records = [System.Collections.Generic.List[object]]::new()
$unresolved = [System.Collections.Generic.List[string]]::new()
$treeCache = @{}
foreach ($key in @($packages.Keys | Sort-Object)) {
    $package = $packages[$key]
    if ($package.hasText) { continue }
    $escapedName = [Uri]::EscapeDataString($package.name).Replace('%2F','%2f')
    $registryUrl = "https://registry.npmjs.org/$escapedName/$($package.version)"
    try { $metadata = Invoke-RestMethod -Uri $registryUrl -UseBasicParsing } catch { $unresolved.Add($key); continue }
    $repository = if ($metadata.repository -is [string]) { $metadata.repository } else { $metadata.repository.url }
    $repository = $repository -replace '#.*$',''
    $commit = [string]$metadata.gitHead
    if (-not $commit -or $repository -notmatch 'github\.com[/:]([^/]+)/([^/#]+?)(?:\.git)?$') { $unresolved.Add($key); continue }
    $owner = $Matches[1]
    $repo = $Matches[2] -replace '\.git$',''
    $license = $null
    foreach ($name in @('LICENSE','LICENSE.md','LICENSE.txt','LICENCE','LICENCE.md','COPYING','license','license.md','license.txt','licence','licence.md','copying')) {
        $candidate = "https://raw.githubusercontent.com/$owner/$repo/$commit/$name"
        try {
            $response = Invoke-WebRequest -Uri $candidate -UseBasicParsing
            if ($response.StatusCode -eq 200 -and $response.Content) { $license = [ordered]@{ url=$candidate; name=$name; content=[string]$response.Content }; break }
        } catch { }
    }
    if (-not $license) {
        $treeKey = "$owner/$repo@$commit"
        if (-not $treeCache.ContainsKey($treeKey)) {
            try { $treeCache[$treeKey] = (Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/git/trees/$commit`?recursive=1" -Headers @{ 'User-Agent'='OpenClicky-license-inventory' }).tree } catch { $treeCache[$treeKey] = @() }
        }
        $candidatePath = @($treeCache[$treeKey] | Where-Object { $_.type -eq 'blob' -and ([IO.Path]::GetFileName($_.path) -match '^(LICENSE|LICENCE|NOTICE|COPYING)(\..*)?$') } | Sort-Object { ($_.path -split '/').Count }, path | Select-Object -First 1).path
        if ($candidatePath) {
            $candidate = "https://raw.githubusercontent.com/$owner/$repo/$commit/$candidatePath"
            try {
                $response = Invoke-WebRequest -Uri $candidate -UseBasicParsing
                if ($response.StatusCode -eq 200 -and $response.Content) { $license = [ordered]@{ url=$candidate; name=[IO.Path]::GetFileName($candidatePath); content=[string]$response.Content } }
            } catch { }
        }
    }
    if (-not $license) { $unresolved.Add($key); continue }
    $safeName = ($package.name -replace '^@', '' -replace '[\\/:*?"<>|@]', '-')
    $directory = Join-Path $output "$safeName@$($package.version)"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $destination = Join-Path $directory $license.name
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($destination, $license.content, $utf8)
    $records.Add([ordered]@{
        name = $package.name
        version = $package.version
        registry = $registryUrl
        repository = $repository
        commit = $commit
        licenseSource = $license.url
        licenseFile = "$safeName@$($package.version)/$($license.name)"
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        evidence = 'License text from the exact npm package repository commit.'
    })
}

$stillUnresolved = [System.Collections.Generic.List[string]]::new()
$spdxCommit = (Invoke-RestMethod -Uri 'https://api.github.com/repos/spdx/license-list-data/commits/main' -Headers @{ 'User-Agent'='OpenClicky-license-inventory' }).sha
foreach ($key in @($unresolved)) {
    $at = $key.LastIndexOf('@')
    $name = $key.Substring(0, $at)
    $version = $key.Substring($at + 1)
    $escapedName = [Uri]::EscapeDataString($name).Replace('%2F','%2f')
    $registryUrl = "https://registry.npmjs.org/$escapedName/$version"
    try { $metadata = Invoke-RestMethod -Uri $registryUrl -UseBasicParsing } catch { $stillUnresolved.Add($key); continue }
    $licenseId = if ($metadata.license) { [string]$metadata.license } elseif ($metadata.licenses -and $metadata.licenses[0].type) { [string]$metadata.licenses[0].type } else { $null }
    if ($licenseId -notin @('MIT','Apache-2.0','CC0-1.0','ISC','BSD-2-Clause','BSD-3-Clause')) { $stillUnresolved.Add($key); continue }
    $licenseSource = "https://raw.githubusercontent.com/spdx/license-list-data/$spdxCommit/text/$licenseId.txt"
    try { $content = (Invoke-WebRequest -Uri $licenseSource -UseBasicParsing).Content } catch { $stillUnresolved.Add($key); continue }
    $safeName = ($name -replace '^@', '' -replace '[\\/:*?"<>|@]', '-')
    $directory = Join-Path $output "$safeName@$version"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $destination = Join-Path $directory "SPDX-$licenseId.txt"
    [IO.File]::WriteAllText($destination, [string]$content, (New-Object Text.UTF8Encoding($false)))
    $repository = if ($metadata.repository -is [string]) { $metadata.repository } else { $metadata.repository.url }
    $records.Add([ordered]@{
        name = $name
        version = $version
        registry = $registryUrl
        repository = $repository
        commit = $spdxCommit
        licenseSource = $licenseSource
        licenseFile = "$safeName@$version/SPDX-$licenseId.txt"
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        evidence = "Exact npm version declares $licenseId; text is the canonical SPDX license at the recorded commit."
    })
}

$provenance = [ordered]@{ schemaVersion=1; records=@($records | Sort-Object name,version); unresolved=@($stillUnresolved) }
[IO.File]::WriteAllText((Join-Path $output 'sources.json'), ($provenance | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Output "Pinned npm fallbacks: $($records.Count); unresolved: $($stillUnresolved.Count); output $output"
