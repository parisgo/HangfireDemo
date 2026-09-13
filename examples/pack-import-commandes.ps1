param([string]$OutputDirectory = "$PSScriptRoot\..\artifacts\plugins")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
dotnet build "$repoRoot\src\HangfireDemo.Core\HangfireDemo.Core.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
$importPath = Join-Path $outputPath ('build-import-' + [guid]::NewGuid().ToString('N'))
dotnet publish "$PSScriptRoot\ImportCommandes.Plugin\ImportCommandes.Plugin.csproj" -c Release -p:Version=1.0.0 -o $importPath
if ($LASTEXITCODE -ne 0) { throw 'Import plugin publish failed.' }
@{
    id='commandes'; version='1.0.0'; entryAssembly='ImportCommandes.Plugin.dll';
    jobs=@(@{id='import-commandes';name='Import commandes';type='ImportCommandes.Plugin.ImportCommandesJob';parametersExample=@{connectionName='ApplicationConnection'}})
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $importPath 'plugin.json') -Encoding utf8
$importZip = Join-Path $outputPath 'import-commandes-1.0.0.zip'
if (Test-Path -LiteralPath $importZip) { Remove-Item -LiteralPath $importZip }
[IO.Compression.ZipFile]::CreateFromDirectory($importPath, $importZip)
Write-Output $importZip
