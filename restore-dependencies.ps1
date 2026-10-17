$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
if (-not $env:SystemRoot) { $env:SystemRoot = [Environment]::GetFolderPath('Windows') }
if (-not $env:WINDIR) { $env:WINDIR = $env:SystemRoot }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageDirectory = Join-Path $taskRoot 'packages'
$libraryDirectory = Join-Path $packageDirectory 'lib'
New-Item -ItemType Directory -Path $libraryDirectory -Force | Out-Null
$dependencies = Get-Content -LiteralPath (Join-Path $taskRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
foreach ($dependency in $dependencies) {
    $packageName = $dependency.id + '.' + $dependency.version + '.nupkg'
    $packagePath = Join-Path $packageDirectory $packageName
    if (-not (Test-Path -LiteralPath $packagePath)) {
        $downloadPath = $packagePath + '.download'
        $packageUrl = 'https://api.nuget.org/v3-flatcontainer/' + $dependency.id + '/' + $dependency.version + '/' + $packageName
        Write-Host ('Downloading ' + $packageName)
        Invoke-WebRequest -Uri $packageUrl -OutFile $downloadPath -UseBasicParsing -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency.sha256) { throw ('Checksum mismatch: ' + $packageName) }
        Move-Item -LiteralPath $downloadPath -Destination $packagePath -Force
    }
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency.sha256) { throw ('Checksum mismatch: ' + $packageName) }
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $prefix = 'lib/' + $dependency.framework + '/'
        $assemblies = @($archive.Entries | Where-Object { $_.FullName.StartsWith($prefix) -and $_.FullName.EndsWith('.dll') -and -not $_.FullName.EndsWith('.Package.dll') })
        if ($assemblies.Count -eq 0) { throw ('Missing framework assemblies: ' + $packageName) }
        foreach ($assembly in $assemblies) {
            $assemblyName = [IO.Path]::GetFileName($assembly.FullName)
            [IO.Compression.ZipFileExtensions]::ExtractToFile($assembly, (Join-Path $libraryDirectory $assemblyName), $true)
        }
    } finally { $archive.Dispose() }
}
Write-Host 'PDF dependencies restored and SHA-256 verified.'
