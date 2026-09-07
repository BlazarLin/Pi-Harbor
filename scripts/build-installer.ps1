# Build a per-user Windows installer from the already tested self-contained package.
param([string]$IsccPath, [string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publishDir = Join-Path $repoRoot 'artifacts/Pi-Harbor-win-x64'
if ($PublishDirectory) { $publishDir = [IO.Path]::GetFullPath($PublishDirectory) }
$outputDir = Join-Path $repoRoot 'artifacts'
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/PIHarness.App/PIHarness.App.csproj') -Encoding UTF8 -Raw
$appVersion = [string]$project.Project.PropertyGroup.Version
$mainExe = Join-Path $publishDir 'Pi-Harbor.exe'
if (-not (Test-Path -LiteralPath $mainExe)) { throw 'Run scripts/build-release.ps1 first.' }
if ((Get-Item -LiteralPath $mainExe).VersionInfo.ProductVersion.Split('+')[0] -ne $appVersion) {
    throw 'Published executable version does not match the project. Rebuild the release first.'
}

if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
        (Join-Path $repoRoot 'artifacts/tools/InnoSetup/ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php or supply -IsccPath.'
}

& $IsccPath "/DAppVersion=$appVersion" "/DPublishDir=$publishDir" "/DOutputDirPath=$outputDir" (Join-Path $repoRoot 'packaging/Pi-Harbor.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$installer = Join-Path $outputDir 'Pi-Harbor-Setup-win-x64.exe'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer was not generated.' }
Write-Output "Installer ready: $installer"
