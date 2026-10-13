param([string]$DotNet = 'dotnet', [string]$Python = 'python', [string]$Iscc = '', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$SkipTests) { & (Join-Path $PSScriptRoot 'build.ps1') -DotNet $DotNet -Python $Python }
$artifacts = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$publish = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
Push-Location $repo
try {
    & $DotNet publish src/LouisaWiFi.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item LICENSE,THIRD_PARTY_NOTICES.md,README.md -Destination $publish
    Copy-Item docs -Destination $publish -Recurse
    # Include the licenses distributed with the exact SDK/runtime used for this build.
    $sdkInfo = & $DotNet --list-sdks
    $sdkLine = $sdkInfo | Where-Object { $_ -match '^10\.0\.401 ' } | Select-Object -First 1
    if (!$sdkLine -or $sdkLine -notmatch '\[(.+)\]') { throw 'Pinned SDK license directory not found' }
    $sdkRoot = Split-Path $Matches[1] -Parent
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'LICENSE.txt') -Destination (Join-Path $publish 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $publish 'DOTNET-THIRD-PARTY-NOTICES.txt')
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'sdk/10.0.401/Sdks/Microsoft.NET.Sdk.WindowsDesktop/LICENSE.TXT') -Destination (Join-Path $publish 'WINDOWSDESKTOP-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'sdk/10.0.401/Sdks/Microsoft.NET.Sdk.WindowsDesktop/THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $publish 'WINDOWSDESKTOP-NOTICES.txt')
    if (!$Iscc) { $Iscc = & (Join-Path $PSScriptRoot 'prepare-inno.ps1') }
    & $Iscc ('/DSourceDir=' + $publish) ('/DOutputDir=' + $artifacts) installer/LouisaWiFi.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
    $zip = Join-Path $artifacts 'LouisaWiFi-win-x64.zip'
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
    @('LouisaWiFi-win-x64.zip','LouisaWiFi-Setup-x64.exe') | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath (Join-Path $artifacts $_) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $_"
    } | Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Packages are in $artifacts"
} finally { Pop-Location }
