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
$output = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$refs = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Security.dll', 'System.Xaml.dll', 'System.Xml.dll')
$wpfRefs = @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')
$common = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/warn:4', '/platform:anycpu')
foreach ($reference in $refs) { $common += '/reference:' + (Join-Path $framework $reference) }
foreach ($reference in $wpfRefs) { $common += '/reference:' + (Join-Path $framework ('WPF\' + $reference)) }
$exe = Join-Path $output 'MyTranslator.exe'
$sources = @('Core.cs','Settings.cs','MainWindow.cs','Program.cs') | ForEach-Object { Join-Path $taskRoot ('src\' + $_) }
& $compiler @common /target:winexe ('/out:' + $exe) ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')) ('/resource:' + (Join-Path $taskRoot 'src\MainWindow.xaml') + ',MainWindow.xaml') @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\App.config') -Destination ($exe + '.config') -Force
Write-Host ('Built: ' + $exe)
if ($Test) {
    $testOutput = Join-Path $taskRoot 'tests\artifacts'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testExe = Join-Path $testOutput 'Translator.Tests.exe'
    & $compiler @common /target:exe ('/out:' + $testExe) ('/reference:' + $exe) (Join-Path $taskRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $testOutput 'MyTranslator.exe') -Force
    Copy-Item -LiteralPath ($exe + '.config') -Destination ($testExe + '.config') -Force
    & $testExe $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
