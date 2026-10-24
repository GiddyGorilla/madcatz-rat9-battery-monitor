[CmdletBinding()]
param(
    [string]$Version = "1.0.3"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$source = Join-Path $root "src"
$output = Join-Path $root "dist"
$manifest = Join-Path $source "app.manifest"
$icon = Join-Path $root "assets\app.ico"

$cscCandidates = [System.Collections.Generic.List[string]]::new()
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $located = & $vswhere `
        -latest `
        -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\Roslyn\csc.exe" |
        Select-Object -First 1
    if ($located) {
        $cscCandidates.Add($located)
    }
}

$cscCandidates.Add(
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe")
$cscCandidates.Add(
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe")
$cscCandidates.Add(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe")

$csc = $cscCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $csc) {
    throw "Roslyn's csc.exe was not found. Install Visual Studio Build Tools."
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

$applicationExe = Join-Path $output "RAT9BatteryTray.exe"
$probeExe = Join-Path $output "RAT9BatteryProbe.exe"
$recoveryExe = Join-Path $output "RAT9RecoveryTool.exe"
$archive = Join-Path $output (
    "MadCatz-RAT9-Battery-Monitor-v$Version-win.zip")

$commonArguments = @(
    "/nologo",
    "/platform:anycpu",
    "/optimize+",
    "/warn:4",
    "/reference:System.dll",
    "/reference:System.Core.dll"
)

$applicationArguments = @(
    "/target:winexe",
    "/out:$applicationExe",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll"
)
if (Test-Path -LiteralPath $manifest) {
    $applicationArguments += "/win32manifest:$manifest"
}
if (Test-Path -LiteralPath $icon) {
    $applicationArguments += "/win32icon:$icon"
}

& $csc @commonArguments @applicationArguments `
    "$source\AssemblyInfo.cs" `
    "$source\BatteryWorkerHost.cs" `
    "$source\Rat9BatteryCore.cs" `
    "$source\Rat9BatteryTray.cs"
if ($LASTEXITCODE -ne 0) {
    throw "Application build failed with exit code $LASTEXITCODE."
}

& $csc @commonArguments `
    "/target:exe" `
    "/out:$probeExe" `
    "$source\Rat9BatteryCore.cs" `
    "$source\BatteryProbe.cs"
if ($LASTEXITCODE -ne 0) {
    throw "Probe build failed with exit code $LASTEXITCODE."
}

$recoveryArguments = @(
    "/target:winexe",
    "/out:$recoveryExe",
    "/win32manifest:$(Join-Path $source 'admin.manifest')",
    "/reference:System.Windows.Forms.dll"
)
if (Test-Path -LiteralPath $icon) {
    $recoveryArguments += "/win32icon:$icon"
}

& $csc @commonArguments @recoveryArguments `
    "$source\RecoveryAssemblyInfo.cs" `
    "$source\Rat9RecoveryTool.cs"
if ($LASTEXITCODE -ne 0) {
    throw "Recovery tool build failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "docs\QUICKSTART.txt") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $root "Install-RAT9-Recovery.cmd") -Destination $output -Force

$releaseFiles = @(
    $applicationExe,
    $probeExe,
    $recoveryExe,
    (Join-Path $output "README.md"),
    (Join-Path $output "LICENSE"),
    (Join-Path $output "QUICKSTART.txt"),
    (Join-Path $output "Install-RAT9-Recovery.cmd")
)

Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -Force

$hashLines = foreach ($file in @(
    $applicationExe,
    $probeExe,
    $recoveryExe,
    $archive)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
}
$hashLines | Set-Content -LiteralPath (
    Join-Path $output "SHA256SUMS.txt") -Encoding ascii

Write-Host "Built $applicationExe"
Write-Host "Built $probeExe"
Write-Host "Built $recoveryExe"
Write-Host "Created $archive"
