param([string]$DotNet = 'dotnet', [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & $Python scripts/fetch_stores.py --validate
    if ($LASTEXITCODE -ne 0) { throw 'Store validation failed' }
    & $Python scripts/test_stores.py
    if ($LASTEXITCODE -ne 0) { throw 'Store tests failed' }
    & $DotNet build src/LouisaWiFi.App -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $DotNet run --project tests/LouisaWiFi.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    & $DotNet run --project tests/LouisaWiFi.Windows.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows tests failed' }
} finally { Pop-Location }
