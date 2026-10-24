[CmdletBinding()]
param(
    [string] $WorkspaceRoot,
    [string] $OpenWorkLicenseRoot,
    [string] $NpmFallbackRoot,
    [Parameter(Mandatory)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $WorkspaceRoot) { $WorkspaceRoot = Join-Path $projectRoot 'apps/companion-workspace' }
if (-not $OpenWorkLicenseRoot) { $OpenWorkLicenseRoot = Join-Path $projectRoot 'packaging/openwork/licenses' }
if (-not $NpmFallbackRoot) { $NpmFallbackRoot = Join-Path $projectRoot 'packaging/third-party-license-fallbacks/npm' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($output) -ne 'THIRD-PARTY-LICENSES') { throw 'OutputDirectory must end in THIRD-PARTY-LICENSES.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
Get-ChildItem -LiteralPath $output -Force | Remove-Item -Recurse -Force

$raw = & pnpm --dir $WorkspaceRoot licenses list --prod --json | Out-String
if ($LASTEXITCODE -ne 0) { throw "pnpm license inventory failed with exit code $LASTEXITCODE." }
$groups = $raw | ConvertFrom-Json
$fallbacks = @{}
$fallbackManifest = Get-Content -Raw -LiteralPath (Join-Path $NpmFallbackRoot 'sources.json') | ConvertFrom-Json
if ($fallbackManifest.unresolved.Count) { throw "Pinned npm fallback inventory remains unresolved: $($fallbackManifest.unresolved -join ', ')" }
foreach ($fallback in $fallbackManifest.records) { $fallbacks["$($fallback.name)@$($fallback.version)"] = $fallback }
$packages = @{}
foreach ($group in $groups.PSObject.Properties) {
    foreach ($summary in $group.Value) {
        foreach ($path in $summary.paths) {
            $metadataPath = Join-Path $path 'package.json'
            if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw "Missing package metadata: $metadataPath" }
            $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
            $key = "$($metadata.name)@$($metadata.version)"
            if (-not $packages.ContainsKey($key)) {
                $repository = if ($metadata.repository -is [string]) { $metadata.repository } elseif ($metadata.repository.url) { $metadata.repository.url } else { $null }
                $packages[$key] = [ordered]@{
                    name = $metadata.name
                    version = $metadata.version
                    license = if ($metadata.license) { [string]$metadata.license } else { [string]$group.Name }
                    source = if ($repository) { $repository } elseif ($metadata.homepage) { $metadata.homepage } else { "https://www.npmjs.com/package/$($metadata.name)/v/$($metadata.version)" }
                    licenseFiles = [System.Collections.Generic.List[string]]::new()
                    licenseSources = [System.Collections.Generic.List[string]]::new()
                    paths = [System.Collections.Generic.List[string]]::new()
                }
            }
            $packages[$key].paths.Add([IO.Path]::GetFullPath($path))
        }
    }
}

$unresolved = [System.Collections.Generic.List[string]]::new()
foreach ($key in @($packages.Keys | Sort-Object)) {
    $item = $packages[$key]
    $safeName = ($item.name -replace '^@', '' -replace '[\\/:*?"<>|@]', '-')
    $packageDirectory = Join-Path $output "$safeName@$($item.version)"
    $seen = @{}
    foreach ($path in $item.paths) {
        foreach ($file in @(Get-ChildItem -LiteralPath $path -File -ErrorAction SilentlyContinue | Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|COPYING|COPYRIGHT)(\.|$)')) {
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            if ($seen.ContainsKey($hash)) { continue }
            $seen[$hash] = $true
            New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
            $destinationName = if ($item.licenseFiles.Count) { "$($item.licenseFiles.Count + 1)-$($file.Name)" } else { $file.Name }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $packageDirectory $destinationName)
            $item.licenseFiles.Add("$safeName@$($item.version)/$destinationName")
            $item.licenseSources.Add("https://www.npmjs.com/package/$($item.name)/v/$($item.version)")
        }
    }
    $item.Remove('paths')
    if (-not $item.licenseFiles.Count -and $fallbacks.ContainsKey($key)) {
        $fallback = $fallbacks[$key]
        $source = Join-Path $NpmFallbackRoot $fallback.licenseFile
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fallback.sha256) { throw "Fallback hash mismatch: $key" }
        New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
        $destinationName = [IO.Path]::GetFileName($source)
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDirectory $destinationName)
        $item.licenseFiles.Add("$safeName@$($item.version)/$destinationName")
        $item.licenseSources.Add([string]$fallback.licenseSource)
    }
    $item.licenseFiles = @($item.licenseFiles)
    $item.licenseSources = @($item.licenseSources | Sort-Object -Unique)
    if (-not $item.licenseFiles.Count) { $unresolved.Add($key) }
}

$assetsPath = Join-Path $projectRoot 'apps/windows/OpenClicky.App/obj/project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) { throw "Restore the app first; missing $assetsPath" }
$assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
$packageRoot = [IO.Path]::GetFullPath(@($assets.packageFolders.PSObject.Properties.Name)[0])
$dotnetPackages = [System.Collections.Generic.List[object]]::new()
$unresolvedDotnet = [System.Collections.Generic.List[string]]::new()
foreach ($library in @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | Sort-Object Name)) {
    $slash = $library.Name.LastIndexOf('/')
    $name = $library.Name.Substring(0, $slash)
    $version = $library.Name.Substring($slash + 1)
    $packagePath = Join-Path $packageRoot (Join-Path $name.ToLowerInvariant() $version)
    $nuspecPath = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1 -ExpandProperty FullName
    if (-not $nuspecPath) { throw "Missing NuGet metadata: $library.Name" }
    [xml]$nuspec = Get-Content -Raw -LiteralPath $nuspecPath
    $metadata = $nuspec.SelectSingleNode("//*[local-name()='metadata']")
    $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
    $license = if ($licenseNode) { [string]$licenseNode.InnerText } else { [string]$metadata.SelectSingleNode("*[local-name()='licenseUrl']").InnerText }
    $repositoryNode = $metadata.SelectSingleNode("*[local-name()='repository']")
    $projectUrlNode = $metadata.SelectSingleNode("*[local-name()='projectUrl']")
    $sourceUrl = if ($repositoryNode.url) { [string]$repositoryNode.url } elseif ($projectUrlNode) { [string]$projectUrlNode.InnerText } else { "https://www.nuget.org/packages/$name/$version" }
    $safeName = $name -replace '[\\/:*?"<>|]','-'
    $destinationDirectory = Join-Path $output "dotnet/$safeName@$version"
    $files = [System.Collections.Generic.List[string]]::new()
    $sources = [System.Collections.Generic.List[string]]::new()
    $candidates = @()
    if ($licenseNode -and $licenseNode.type -eq 'file') { $candidates += Get-Item -LiteralPath (Join-Path $packagePath $licenseNode.InnerText) -ErrorAction SilentlyContinue }
    $candidates += Get-ChildItem -LiteralPath $packagePath -File -ErrorAction SilentlyContinue | Where-Object Name -Match '^(LICENSE|LICENCE|NOTICE|COPYING|COPYRIGHT)(\.|$)'
    foreach ($file in @($candidates | Where-Object { $_ } | Sort-Object FullName -Unique)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destinationDirectory $file.Name)
        $files.Add("dotnet/$safeName@$version/$($file.Name)")
        $sources.Add("https://www.nuget.org/packages/$name/$version")
    }
    if (-not $files.Count -and $license -in @('MIT','Apache-2.0','CC0-1.0','ISC','BSD-2-Clause','BSD-3-Clause')) {
        $spdxFile = Get-ChildItem -LiteralPath $NpmFallbackRoot -Recurse -File -Filter "SPDX-$license.txt" | Select-Object -First 1
        if ($spdxFile) {
            $spdxRelative = $spdxFile.FullName.Substring($NpmFallbackRoot.Length + 1) -replace '\\','/'
            $spdxRecord = $fallbackManifest.records | Where-Object licenseFile -eq $spdxRelative | Select-Object -First 1
        }
        if ($spdxRecord) {
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            Copy-Item -LiteralPath $spdxFile.FullName -Destination (Join-Path $destinationDirectory $spdxFile.Name)
            $files.Add("dotnet/$safeName@$version/$($spdxFile.Name)")
            $sources.Add([string]$spdxRecord.licenseSource)
        }
    }
    if (-not $files.Count) { $unresolvedDotnet.Add($library.Name) }
    $dotnetPackages.Add([ordered]@{ name=$name; version=$version; license=$license; source=$sourceUrl; licenseFiles=@($files); licenseSources=@($sources | Sort-Object -Unique) })
}

$runtimeDirectory = Join-Path $output 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
$runtime = @(
    [ordered]@{ name='openwork-server'; version='0.18.57'; license='MIT'; source='https://github.com/different-ai/openwork'; file='OPENWORK-LICENSE.txt' },
    [ordered]@{ name='opencode'; version='1.18.30'; license='MIT'; source='https://github.com/anomalyco/opencode'; file='OPENCODE-LICENSE.txt' },
    [ordered]@{ name='node'; version='24.18.1'; license='Node.js license and bundled third-party notices'; source='https://nodejs.org/dist/v24.18.1/'; file='NODE-LICENSE.txt' }
)
foreach ($item in $runtime) {
    $source = Join-Path $OpenWorkLicenseRoot $item.file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing runtime notice: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $runtimeDirectory $item.file)
}

$manifest = [ordered]@{
    schemaVersion = 1
    description = 'Production JavaScript dependency inventory from pnpm plus bundled OpenWork runtime notices.'
    packages = @($packages.Keys | Sort-Object | ForEach-Object { $packages[$_] })
    dotnetPackages = @($dotnetPackages)
    runtime = $runtime
    unresolvedLicenseText = @($unresolved)
    unresolvedDotnetLicenseText = @($unresolvedDotnet)
}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10), $utf8)
Write-Output "Third-party inventory: $($packages.Count) npm and $($dotnetPackages.Count) NuGet packages; unresolved npm $($unresolved.Count), NuGet $($unresolvedDotnet.Count); output $output"
