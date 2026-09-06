# Created: 2026-09-06
# Purpose: Test and publish the self-contained Windows x64 PI-Harness package.

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$publishDir = Join-Path $artifactsRoot 'PI-Harness-win-x64'
$zipPath = Join-Path $artifactsRoot 'PI-Harness-win-x64.zip'
$testScript = Join-Path $PSScriptRoot 'run-tests.ps1'
$appProject = Join-Path $repoRoot 'src\PIHarness.App\PIHarness.App.csproj'

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

powershell -NoProfile -ExecutionPolicy Bypass -File $testScript
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
Remove-OwnedPath $publishDir
Remove-OwnedPath $zipPath

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

$mainExecutable = Join-Path $publishDir 'PI-Harness.exe'
if (-not (Test-Path -LiteralPath $mainExecutable)) {
    throw 'PI-Harness.exe was not generated'
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$fileCount = (Get-ChildItem -LiteralPath $publishDir -File).Count
$packageSizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Write-Output "Release package ready: $publishDir"
Write-Output "Files: $fileCount; ZIP: $zipPath ($packageSizeMb MB)"
