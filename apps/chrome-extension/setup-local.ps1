param(
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $NativeHostPath,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string[]] $AllowedOrigins,
    [switch] $GenerationOnly
)

$ErrorActionPreference = 'Stop'
$ExtensionId = 'demdidhcdmmfbpidanflhknhplklkcja'
$source = Split-Path -Parent $PSCommandPath
$install = Join-Path $env:LOCALAPPDATA 'OpenClicky\ChromeBridge'
New-Item -ItemType Directory -Force -Path $install | Out-Null

$exactOrigins = foreach ($origin in ($AllowedOrigins | ForEach-Object { $_ -split ',' })) {
    $origin = $origin.Trim()
    $uri = [Uri]$origin
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -notin @('https', 'http') -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment -or $uri.UserInfo) {
        throw "AllowedOrigins entries must be exact HTTP(S) origins ending in /, for example https://example.com/"
    }
    "{0}://{1}/" -f $uri.Scheme, $uri.Authority
}
$matches = $exactOrigins | ForEach-Object { $_ + '*' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$manifest = Get-Content -Raw -LiteralPath (Join-Path $source 'manifest.template.json') | ConvertFrom-Json
$manifest.content_scripts[0].matches = @($matches)
[IO.File]::WriteAllText((Join-Path $install 'manifest.json'), ($manifest | ConvertTo-Json -Depth 20), $utf8)
Copy-Item -LiteralPath (Join-Path $source 'service-worker.js') -Destination $install -Force
Copy-Item -LiteralPath (Join-Path $source 'content-script.js') -Destination $install -Force

$nativeHostManifest = Get-Content -Raw -LiteralPath (Join-Path $source 'native-host.template.json') | ConvertFrom-Json
$nativeHostManifest.path = (Resolve-Path -LiteralPath $NativeHostPath).Path
$nativeHostManifest.allowed_origins = @("chrome-extension://$ExtensionId/")
$hostManifest = Join-Path $install 'native-host.json'
[IO.File]::WriteAllText($hostManifest, ($nativeHostManifest | ConvertTo-Json -Depth 10), $utf8)
$config = @{ extensionOrigin = "chrome-extension://$ExtensionId/"; allowedOrigins = @($exactOrigins) } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $install 'bridge-config.json'), $config, $utf8)
if (-not $GenerationOnly) {
    $registry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.openclicky.chrome_bridge'
    New-Item -Path $registry -Force | Out-Null
    Set-Item -Path $registry -Value $hostManifest
}

Write-Host "Generated extension: $install"
if ($GenerationOnly) { Write-Host 'Generation-only mode: native-host registry was not changed.'; exit 0 }
Write-Host 'In chrome://extensions, enable Developer mode, choose Load unpacked, and select that folder.'
Write-Host 'Chrome will ask for site access only for the exact origins supplied to this script.'
