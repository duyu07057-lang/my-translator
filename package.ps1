$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$exePath = Join-Path $taskRoot 'dist\MyTranslator.exe'
if (-not (Test-Path -LiteralPath $exePath)) { & (Join-Path $taskRoot 'build.ps1') }
$releaseRoot = Join-Path $taskRoot 'release'
$packageRoot = Join-Path $releaseRoot 'MyTranslator-Windows-v1.0'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath $exePath -Destination (Join-Path $packageRoot 'MyTranslator.exe') -Force
Copy-Item -LiteralPath ($exePath + '.config') -Destination (Join-Path $packageRoot 'MyTranslator.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $packageRoot 'README.md') -Force
$packageImages = Join-Path $packageRoot 'docs\images'
New-Item -ItemType Directory -Path $packageImages -Force | Out-Null
foreach ($imageName in @('home.png', 'glossary.png')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\images\' + $imageName)) -Destination (Join-Path $packageImages $imageName) -Force
}
Set-Content -LiteralPath (Join-Path $packageRoot 'Start.cmd') -Encoding Ascii -Value '@echo off', 'start "" "%~dp0MyTranslator.exe"'
$zipPath = Join-Path $releaseRoot 'MyTranslator-Windows-v1.0.zip'
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force
Write-Host ('Packaged: ' + $zipPath)
