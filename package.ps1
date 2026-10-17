$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$exePath = Join-Path $taskRoot 'dist\MyTranslator.exe'
$runtimeManifestPath = Join-Path $taskRoot 'dist\runtime-dependencies.json'
if (-not (Test-Path -LiteralPath $exePath) -or -not (Test-Path -LiteralPath $runtimeManifestPath)) { & (Join-Path $taskRoot 'build.ps1') }
$releaseRoot = Join-Path $taskRoot 'release'
$packageRoot = Join-Path $releaseRoot 'MyTranslator-Windows-v1.1.0'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath $exePath -Destination (Join-Path $packageRoot 'MyTranslator.exe') -Force
Copy-Item -LiteralPath ($exePath + '.config') -Destination (Join-Path $packageRoot 'MyTranslator.exe.config') -Force
$runtimeDependencies = Get-Content -LiteralPath $runtimeManifestPath -Raw | ConvertFrom-Json
foreach ($dependency in $runtimeDependencies) {
    if ([IO.Path]::GetFileName($dependency.name) -ne $dependency.name -or $dependency.name -notlike '*.dll') { throw 'Invalid runtime dependency manifest.' }
    $libraryPath = Join-Path (Join-Path $taskRoot 'dist') $dependency.name
    if ((Get-FileHash -LiteralPath $libraryPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency.sha256) { throw ('Runtime library checksum mismatch: ' + $dependency.name) }
    Copy-Item -LiteralPath $libraryPath -Destination (Join-Path $packageRoot $dependency.name) -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $packageRoot 'README.md') -Force
$packageImages = Join-Path $packageRoot 'docs\images'
New-Item -ItemType Directory -Path $packageImages -Force | Out-Null
foreach ($imageName in @('home.png', 'glossary.png', 'document.png')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\images\' + $imageName)) -Destination (Join-Path $packageImages $imageName) -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $packageRoot 'THIRD_PARTY_NOTICES.md') -Force
$packageLicenses = Join-Path $packageRoot 'third_party\licenses'
New-Item -ItemType Directory -Path $packageLicenses -Force | Out-Null
foreach ($licenseName in @('PdfPig-LICENSE.txt', 'PdfPig-NOTICES.txt', 'Microsoft-MIT-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot ('third_party\licenses\' + $licenseName)) -Destination (Join-Path $packageLicenses $licenseName) -Force
}
Set-Content -LiteralPath (Join-Path $packageRoot 'Start.cmd') -Encoding Ascii -Value '@echo off', 'start "" "%~dp0MyTranslator.exe"'
$zipPath = Join-Path $releaseRoot 'MyTranslator-Windows-v1.1.0.zip'
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force
Write-Host ('Packaged: ' + $zipPath)
