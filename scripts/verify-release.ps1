# Verify downloaded GitHub Release assets without touching an existing installation.
param(
    [Parameter(Mandatory=$true)][string]$AssetDirectory,
    [Parameter(Mandatory=$true)][string]$VerificationDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [string]$ExpectedCommit
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$assetDir = [IO.Path]::GetFullPath($AssetDirectory)
$qaDir = [IO.Path]::GetFullPath($VerificationDirectory)
if (-not $qaDir.StartsWith($artifactsRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'VerificationDirectory must be inside artifacts.' }
if (Test-Path -LiteralPath $qaDir) { throw 'Use a new verification directory.' }
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{194394EC-A8BE-4DAB-8E69-B1B73DD96B1F}_is1'
if (Test-Path -LiteralPath $registryPath) { throw 'An existing installation is registered; verification will not replace it.' }
New-Item -ItemType Directory -Path $qaDir | Out-Null
$report = [Collections.Generic.List[string]]::new()
function Record([string]$message) { $report.Add($message); Write-Output $message; [IO.File]::WriteAllLines((Join-Path $qaDir 'verification.txt'), $report, [Text.UTF8Encoding]::new($false)) }
function Run-Checked([string]$file, [string[]]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw "Timed out: $file" }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Exit $($process.ExitCode): $file" }
}
$expectedNames = @('Pi-Harbor-win-x64.zip', 'Pi-Harbor-Setup-win-x64.exe')
$checksums = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $assetDir 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  (Pi-Harbor-(?:win-x64\.zip|Setup-win-x64\.exe))$') { throw 'Unexpected checksum line' }
    if ($checksums.ContainsKey($Matches[2])) { throw 'Duplicate checksum entry' }
    $checksums[$Matches[2]] = $Matches[1]
}
foreach ($name in $expectedNames) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $assetDir $name) -Algorithm SHA256).Hash
    if ($hash -ne $checksums[$name]) { throw "Checksum mismatch: $name" }
    Record "PASS SHA256 $name $hash"
}
$portableDir = Join-Path $qaDir 'portable'
$installDir = Join-Path $qaDir 'installed'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $assetDir 'Pi-Harbor-win-x64.zip'))
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $destination = [IO.Path]::GetFullPath((Join-Path $portableDir $entry.FullName))
        if (-not $destination.StartsWith($portableDir + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe ZIP path' }
        if (-not $names.Add($entry.FullName)) { throw 'Duplicate ZIP entry' }
        if ($entry.FullName -match '(^|[/\\])(\.pi|\.git|auth\.json|\.env)([/\\]|$)|\.(jsonl|pfx|p12|key|log)$') { throw "Unexpected private/runtime data: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $assetDir 'Pi-Harbor-win-x64.zip'), $portableDir)
$app = Join-Path $portableDir 'Pi-Harbor.exe'
$version = (Get-Item -LiteralPath $app).VersionInfo.ProductVersion
if ($version.Split('+')[0] -ne $ExpectedVersion) { throw "Version mismatch: $version" }
if ($ExpectedCommit -and $version -ne "$ExpectedVersion+$ExpectedCommit") { throw "Commit mismatch: $version" }
foreach ($required in @('Pi-Harbor.dll','coreclr.dll','hostfxr.dll','PresentationFramework.dll','LICENSE','THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $portableDir $required))) { throw "Missing package file: $required" }
}
if (@(Get-ChildItem -LiteralPath (Join-Path $portableDir 'licenses') -Recurse -File).Count -lt 3) { throw 'Missing runtime licenses' }
$runtime = Get-Content -LiteralPath (Join-Path $portableDir 'Pi-Harbor.runtimeconfig.json') -Raw | ConvertFrom-Json
if (@($runtime.runtimeOptions.includedFrameworks).Count -lt 2) { throw 'Package is not self-contained' }
Record "PASS portable content, runtime licenses, version $version"
Run-Checked $app @('--capture-public-docs', ('"' + (Join-Path $qaDir 'portable-ui') + '"'))
if (-not (Test-Path -LiteralPath (Join-Path $qaDir 'portable-ui/overview.png'))) { throw 'Portable UI capture missing' }
Record 'PASS portable startup and public UI checks'
$manifest = @(Get-ChildItem -LiteralPath $portableDir -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path=$_.FullName.Substring($portableDir.Length + 1); Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
foreach ($pass in 1..2) {
    Run-Checked (Join-Path $assetDir 'Pi-Harbor-Setup-win-x64.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS','/NOICONS','/TASKS=', ('/DIR="' + $installDir + '"'), ('/LOG="' + (Join-Path $qaDir "install-$pass.log") + '"'))
    if ((Get-ItemProperty -LiteralPath $registryPath).InstallLocation.TrimEnd('\') -ne $installDir) { throw 'Unexpected install registration' }
    foreach ($file in $manifest) {
        if ((Get-FileHash -LiteralPath (Join-Path $installDir $file.Path) -Algorithm SHA256).Hash -ne $file.Hash) { throw "Installed content mismatch: $($file.Path)" }
    }
    Record "PASS installation $pass, all $($manifest.Count) installed files match portable ZIP"
}
Run-Checked (Join-Path $installDir 'Pi-Harbor.exe') @('--capture-ui', ('"' + (Join-Path $qaDir 'installed-startup.png') + '"'))
if (-not (Test-Path -LiteralPath (Join-Path $qaDir 'installed-startup.png'))) { throw 'Installed startup capture missing' }
Record 'PASS installed application startup'
Run-Checked (Join-Path $installDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/LOG="' + (Join-Path $qaDir 'uninstall.log') + '"'))
if ((Test-Path -LiteralPath (Join-Path $installDir 'Pi-Harbor.exe')) -or (Test-Path -LiteralPath $registryPath)) { throw 'Incomplete uninstall' }
Record 'PASS uninstall removes application and registration'
