param([string]$OutputDirectory = "$PSScriptRoot\..\artifacts\plugins")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = (Resolve-Path -LiteralPath "$PSScriptRoot\..").Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
dotnet build "$repoRoot\src\HangfireDemo.Core\HangfireDemo.Core.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
foreach ($version in @('1.0.0','2.0.0','2.0.1','2.0.2')) {
    $publishPath = Join-Path $outputPath ("build-" + $version + '-' + [guid]::NewGuid().ToString('N'))
    dotnet publish "$PSScriptRoot\SendReport.Plugin\SendReport.Plugin.csproj" -c Release -p:Version=$version -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Plugin publish failed.' }
    @{
        id = 'reports'; version = $version; entryAssembly = 'SendReport.Plugin.dll';
        jobs = @(@{id='send-report'; name='Send report (simulation)'; type='SendReport.Plugin.SendReport'; parametersExample=@{reportName='Monthly report';recipient='demo@example.com';simulationSeconds=1}})
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publishPath 'plugin.json') -Encoding utf8
    $zipPath = Join-Path $outputPath "send-report-$version.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    [IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $zipPath)
    Write-Output $zipPath
}
