param([switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$windowsDirectory = [Environment]::GetFolderPath('Windows')
if (-not $env:SystemRoot) { $env:SystemRoot = $windowsDirectory }
if (-not $env:WINDIR) { $env:WINDIR = $windowsDirectory }
$framework = Join-Path $windowsDirectory 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $framework = Join-Path $windowsDirectory 'Microsoft.NET\Framework\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 is required.' }
& (Join-Path $taskRoot 'restore-dependencies.ps1')
$output = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$refs = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Security.dll', 'System.Xaml.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Numerics.dll')
$wpfRefs = @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')
$common = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/warn:4', '/platform:anycpu')
foreach ($reference in $refs) { $common += '/reference:' + (Join-Path $framework $reference) }
foreach ($reference in $wpfRefs) { $common += '/reference:' + (Join-Path $framework ('WPF\' + $reference)) }
$dependencyAssemblies = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'packages\lib') -Filter '*.dll')
foreach ($assembly in $dependencyAssemblies) { $common += '/reference:' + $assembly.FullName }
$exe = Join-Path $output 'MyTranslator.exe'
$sources = @('Core.cs','Settings.cs','Documents.cs','MainWindow.cs','Program.cs') | ForEach-Object { Join-Path $taskRoot ('src\' + $_) }
& $compiler @common /target:winexe ('/out:' + $exe) ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')) ('/resource:' + (Join-Path $taskRoot 'src\MainWindow.xaml') + ',MainWindow.xaml') @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\App.config') -Destination ($exe + '.config') -Force
foreach ($assembly in $dependencyAssemblies) { Copy-Item -LiteralPath $assembly.FullName -Destination (Join-Path $output $assembly.Name) -Force }
$dependencyManifest = @($dependencyAssemblies | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
ConvertTo-Json -InputObject $dependencyManifest | Set-Content -LiteralPath (Join-Path $output 'runtime-dependencies.json') -Encoding UTF8
Write-Host ('Built: ' + $exe)
if ($Test) {
    $testOutput = Join-Path $taskRoot 'tests\artifacts'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testExe = Join-Path $testOutput 'Translator.Tests.exe'
    & $compiler @common /target:exe ('/out:' + $testExe) ('/reference:' + $exe) (Join-Path $taskRoot 'tests\Tests.cs') (Join-Path $taskRoot 'tests\DocumentTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $testOutput 'MyTranslator.exe') -Force
    Copy-Item -LiteralPath ($exe + '.config') -Destination ($testExe + '.config') -Force
    foreach ($assembly in $dependencyAssemblies) { Copy-Item -LiteralPath $assembly.FullName -Destination (Join-Path $testOutput $assembly.Name) -Force }
    & $testExe $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
