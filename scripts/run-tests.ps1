# Created: 2026-09-06
# Purpose: Build and run the dependency-free PI-Harness test executable.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests\PIHarness.Tests\PIHarness.Tests.csproj'

dotnet run --project $testProject -c Release -- @args
if ($LASTEXITCODE -ne 0) {
    throw "PI-Harness tests failed with exit code $LASTEXITCODE"
}
