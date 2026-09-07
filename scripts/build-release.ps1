# Created: 2026-09-06
# Purpose: Test and publish the self-contained Windows x64 Pi Harbor package.

param([switch]$IncludeInstaller, [switch]$SkipSmokeCapture, [string]$IsccPath, [string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifactsRoot 'Pi-Harbor-win-x64'
if ($PublishDirectory) { $publishDir = [IO.Path]::GetFullPath($PublishDirectory) }
if (-not $publishDir.StartsWith($artifactsRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'PublishDirectory must be a child of artifacts.'
}
# Check before any removal so a user can keep the previous build open.
$runningApp = Get-Process -Name 'Pi-Harbor' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($publishDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}
if ($runningApp) { throw 'This publish directory is running. Use -PublishDirectory artifacts/Pi-Harbor-next-win-x64 to keep it open.' }
$zipPath = Join-Path $artifactsRoot 'Pi-Harbor-win-x64.zip'
$testScript = Join-Path $PSScriptRoot 'run-tests.ps1'
$iconScript = Join-Path $PSScriptRoot 'generate-app-icon.ps1'
$appProject = Join-Path $repoRoot 'src\PIHarness.App\PIHarness.App.csproj'
$smokeCapture = Join-Path $artifactsRoot 'Pi-Harbor-release-smoke.png'

function Remove-OwnedPath([string]$targetPath) {
    if (-not (Test-Path -LiteralPath $targetPath)) {
        return
    }

    $resolvedTarget = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $targetPath).Path)
    $requiredPrefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolvedTarget.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts: $resolvedTarget"
    }

    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

powershell -NoProfile -ExecutionPolicy Bypass -File $iconScript
if ($LASTEXITCODE -ne 0) {
    throw "Icon generation failed with exit code $LASTEXITCODE"
}

powershell -NoProfile -ExecutionPolicy Bypass -File $testScript
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
Remove-OwnedPath $publishDir
Remove-OwnedPath $zipPath
Remove-OwnedPath $smokeCapture

dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=portable `
    -p:DebugSymbols=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

$mainExecutable = Join-Path $publishDir 'Pi-Harbor.exe'
if (-not (Test-Path -LiteralPath $mainExecutable)) {
    throw 'Pi-Harbor.exe was not generated'
}

if (-not $SkipSmokeCapture) {
$smokeProcess = Start-Process -FilePath $mainExecutable `
    -ArgumentList @('--capture-ui', $smokeCapture) `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($smokeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeCapture)) {
    throw "Pi Harbor startup smoke test failed with exit code $($smokeProcess.ExitCode)"
}
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDir

# Runtime packs are package downloads, not necessarily listed in assets.libraries.
# Read the versions actually bundled by publish and fail if their licenses are missing.
$assets = Get-Content -LiteralPath (Join-Path $repoRoot 'src/PIHarness.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDir 'Pi-Harbor.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
if ($frameworks.Count -lt 2) { throw 'Expected self-contained .NET and Windows Desktop runtime versions.' }
foreach ($framework in $frameworks) {
    $packageId = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
    $packageDir = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($packageId + '/' + $framework.version)
        if (Test-Path -LiteralPath $candidate) { $packageDir = $candidate; break }
    }
    if (-not $packageDir) { throw "Runtime license source not found: $packageId/$($framework.version)" }
    $licenseDir = Join-Path $publishDir ('licenses/' + $packageId)
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    $licenseFiles = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })
    if (-not ($licenseFiles | Where-Object { $_.Name -match '^LICENSE(\.TXT)?$' })) { throw "Missing license: $packageId" }
    $licenseFiles | Copy-Item -Destination $licenseDir
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$fileCount = (Get-ChildItem -LiteralPath $publishDir -File).Count
$packageSizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Write-Output "Release package ready: $publishDir"
Write-Output "Files: $fileCount; ZIP: $zipPath ($packageSizeMb MB)"

$releaseFiles = @($zipPath)
if ($IncludeInstaller) {
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -IsccPath $IsccPath -PublishDirectory $publishDir
    $releaseFiles += Join-Path $artifactsRoot 'Pi-Harbor-Setup-win-x64.exe'
}
$hashLines = foreach ($file in $releaseFiles) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($file))"
}
[IO.File]::WriteAllLines((Join-Path $artifactsRoot 'SHA256SUMS.txt'), [string[]]$hashLines, [Text.UTF8Encoding]::new($false))
