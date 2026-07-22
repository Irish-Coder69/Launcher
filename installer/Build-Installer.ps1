# Build Launcher Installer
# This script compiles the NSIS installer script into an executable installer

[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification='Interactive build script requires colored console output for user feedback.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification='Dynamic command execution needed for flexible NSIS compilation.')]
param(
    [switch]$Force,
    [switch]$OpenAfterBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Configuration
$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $installerDir
$nativeDir = Join-Path $projectRoot "native"
$publishScript = Join-Path $nativeDir "Publish-NativeApp.ps1"
$nativePublishOutput = Join-Path $nativeDir "publish\win-x64"
$nativeExecutable = Join-Path $nativePublishOutput "Launcher.App.exe"
$nsiScript = Join-Path $installerDir "Launcher.nsi"
$outputDir = Join-Path $installerDir "Output"
$nsiVersionMatch = Select-String -Path $nsiScript -Pattern '!define PRODUCT_VERSION "([^"]+)"' -AllMatches | Select-Object -First 1
if (-not $nsiVersionMatch -or $nsiVersionMatch.Matches.Count -eq 0) {
    throw "Could not determine PRODUCT_VERSION from $nsiScript"
}
$productVersion = $nsiVersionMatch.Matches[0].Groups[1].Value
$expectedInstallerName = "Launcher-$productVersion-Setup.exe"
$expectedInstallerPath = Join-Path $outputDir $expectedInstallerName

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Launcher Installer Build Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if NSIS is installed
Write-Host "Checking for NSIS installation..." -ForegroundColor Yellow

$cmd = Get-Command makensis.exe -ErrorAction SilentlyContinue
$nsisPaths = @(
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files\NSIS\makensis.exe"
)

if ($cmd -and $cmd.Source) {
    $nsisPaths += $cmd.Source
}

$makensis = $null
foreach ($path in $nsisPaths) {
    if ($path -and (Test-Path $path)) {
        $makensis = $path
        break
    }
}

if (-not $makensis) {
    Write-Host "ERROR: NSIS is not installed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "To install NSIS:" -ForegroundColor Yellow
    Write-Host "1. Download from: https://nsis.sourceforge.io/Download" -ForegroundColor Gray
    Write-Host "2. Run the installer" -ForegroundColor Gray
    Write-Host "3. Run this script again" -ForegroundColor Gray
    exit 1
}

Write-Host "✓ Found NSIS: $makensis" -ForegroundColor Green
Write-Host ""

# Verify required files exist
Write-Host "Verifying required files..." -ForegroundColor Yellow

$requiredFiles = @(
    (Join-Path $projectRoot "launcher.ps1"),
    (Join-Path $projectRoot "launcher.config.json"),
    $publishScript,
    $nsiScript,
    (Join-Path $installerDir "LICENSE.txt")
)

$missingFiles = @()
foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "✓ $(Split-Path -Leaf $file)" -ForegroundColor Green
    } else {
        Write-Host "✗ $(Split-Path -Leaf $file) - MISSING" -ForegroundColor Red
        $missingFiles += $file
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: Missing required files!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Publishing native WPF app..." -ForegroundColor Yellow
& $publishScript -Configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Native app publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $nativeExecutable)) {
    throw "Native executable was not found after publish: $nativeExecutable"
}

Write-Host ""

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
    Write-Host "Created output directory: $outputDir" -ForegroundColor Green
} else {
    Write-Host "Output directory: $outputDir" -ForegroundColor Green
}

# Clean previous build (optional)
$existingInstaller = Get-Item -LiteralPath $expectedInstallerPath -ErrorAction SilentlyContinue
if ($existingInstaller -and -not $Force) {
    Write-Host "Warning: Existing installer found. Use -Force to rebuild." -ForegroundColor Yellow
    exit 0
}

if ($Force) {
    Get-ChildItem $outputDir -Filter "Launcher-*-Setup.exe" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Yellow

# Build the installer
$buildLog = Join-Path $installerDir "build.log"

Write-Host "Running: $makensis" -ForegroundColor Gray
Write-Host "  Script: $(Split-Path -Leaf $nsiScript)" -ForegroundColor Gray
Write-Host "  Output: $outputDir" -ForegroundColor Gray
Write-Host ""

# Execute the build
& $makensis /O"$buildLog" /DOUTDIR="$outputDir" "$nsiScript"
$buildSuccess = $LASTEXITCODE -eq 0

Write-Host ""

if ($buildSuccess) {
    Write-Host "✓ Build completed successfully!" -ForegroundColor Green
    Write-Host ""

    # Show the generated installer
    $installers = Get-Item -LiteralPath $expectedInstallerPath -ErrorAction SilentlyContinue
    if (-not $installers) {
        # Fallback for scripts that output in installerDir
        $fallback = Get-Item -LiteralPath (Join-Path $installerDir $expectedInstallerName) -ErrorAction SilentlyContinue
        if ($fallback) {
            Move-Item -Path $fallback.FullName -Destination $expectedInstallerPath -Force
            $installers = Get-Item -LiteralPath $expectedInstallerPath -ErrorAction SilentlyContinue
        }
    }

    if ($installers) {
        Write-Host "Generated installer(s):" -ForegroundColor Cyan
        $size = [Math]::Round(($installers.Length / 1MB), 2)
        Write-Host "  - $($installers.Name) ($size MB)" -ForegroundColor Green

        Write-Host ""
        Write-Host "To install the Launcher:" -ForegroundColor Cyan
        Write-Host "  1. Run: $($installers.FullName)" -ForegroundColor Gray
        Write-Host "  2. Follow the installation wizard" -ForegroundColor Gray
        Write-Host "  3. Choose your installation directory" -ForegroundColor Gray
        Write-Host "  4. Select desired components (Start Menu, Desktop shortcuts)" -ForegroundColor Gray
        Write-Host ""

        if ($OpenAfterBuild) {
            Start-Process $installers.FullName
            Write-Host "Opening installer..." -ForegroundColor Green
        }
    } else {
        Write-Host "Build succeeded, but no installer was found in output locations." -ForegroundColor Yellow
    }
} else {
    Write-Host "✗ Build failed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Build log: $buildLog" -ForegroundColor Yellow
    Write-Host ""

    # Display build log if it exists
    if (Test-Path $buildLog) {
        Write-Host "--- Build Log Output ---" -ForegroundColor Yellow
        Get-Content $buildLog | Write-Host
    }

    exit 1
}

