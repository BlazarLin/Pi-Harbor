# Created: 2026-09-06
# Purpose: Build and run the dependency-free Pi Harbor test executable.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests\PIHarness.Tests\PIHarness.Tests.csproj'

dotnet run --project $testProject -c Release -- @args
if ($LASTEXITCODE -ne 0) {
    throw "Pi Harbor tests failed with exit code $LASTEXITCODE"
}
