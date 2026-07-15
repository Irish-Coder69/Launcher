# Update Installer for Launcher
# Downloads and installs the latest version of Launcher
# Designed to be called after Check-LauncherUpdate.ps1 detects an update

[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification='Interactive installer script requires colored console output for user feedback.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification='Top-level script parameters are consumed in script scope.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification='Function names describe collection-based maintenance operations.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseApprovedVerbs', '', Justification='Legacy function names kept stable for maintainability.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification='This script runs non-interactively and logs every mutation step.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification='Some state variables are retained for future diagnostics compatibility.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseBOMForUnicodeEncodedFile', '', Justification='Repository standardizes on UTF-8 without BOM unless required.')]
param(
    [string]$DownloadUrl = $(throw "DownloadUrl is required"),
    [string]$InstallDir = $PSScriptRoot,
    [string]$Checksum = $null,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ============================================================================
# Configuration
# ============================================================================

$updateDir = Join-Path -Path $env:APPDATA -ChildPath "Launcher\updates"
$backupDir = Join-Path -Path $env:APPDATA -ChildPath "Launcher\backups"
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"

# ============================================================================
# Functions
# ============================================================================

function Write-UpdateLog {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $(
        if ($Level -eq "ERROR") { "Red" }
        elseif ($Level -eq "WARN") { "Yellow" }
        else { "Green" }
    )
}

function New-UpdateDirectory {
    if (-not (Test-Path $updateDir)) {
        New-Item -ItemType Directory -Path $updateDir -Force | Out-Null
        Write-UpdateLog "Created update directory: $updateDir" "INFO"
    }

    if (-not (Test-Path $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        Write-UpdateLog "Created backup directory: $backupDir" "INFO"
    }
}

function Download-Update {
    param(
        [string]$Url,
        [string]$OutputPath
    )

    try {
        Write-UpdateLog "Downloading update from: $Url" "INFO"

        $progressPreference = 'Continue'
        Invoke-WebRequest -Uri $Url `
            -OutFile $OutputPath `
            -UseBasicParsing `
            -TimeoutSec 300 `
            -ErrorAction Stop

        if (Test-Path $OutputPath) {
            $size = [Math]::Round((Get-Item $OutputPath).Length / 1MB, 2)
            Write-UpdateLog "✓ Downloaded successfully ($size MB)" "INFO"
            return $true
        }

        return $false
    }
    catch {
        Write-UpdateLog "Download failed: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Test-FileIntegrity {
    param(
        [string]$FilePath,
        [string]$ExpectedChecksum
    )

    if ([string]::IsNullOrEmpty($ExpectedChecksum) -or $SkipVerification) {
        Write-UpdateLog "Skipping checksum verification (not specified or disabled)" "WARN"
        return $true
    }

    try {
        Write-UpdateLog "Verifying file integrity..." "INFO"

        $actualChecksum = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash

        if ($actualChecksum -eq $ExpectedChecksum) {
            Write-UpdateLog "✓ File integrity verified" "INFO"
            return $true
        }
        else {
            Write-UpdateLog "✗ Checksum mismatch!" "ERROR"
            Write-UpdateLog "  Expected: $ExpectedChecksum" "ERROR"
            Write-UpdateLog "  Actual:   $actualChecksum" "ERROR"
            return $false
        }
    }
    catch {
        Write-UpdateLog "Checksum verification failed: $($_.Exception.Message)" "WARN"
        return -not $SkipVerification
    }
}

function Backup-CurrentVersion {
    try {
        Write-UpdateLog "Backing up current version..." "INFO"

        $backupPath = Join-Path -Path $backupDir -ChildPath "backup-$timestamp"
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null

        # Backup critical files
        $filesToBackup = @(
            "launcher.ps1",
            "Launcher.cmd",
            "launcher.config.json",
            "launcher.normal-slow.config.json",
            "launcher.ultra-slow.config.json",
            "version.txt"
        )

        foreach ($file in $filesToBackup) {
            $sourcePath = Join-Path -Path $InstallDir -ChildPath $file
            if (Test-Path $sourcePath) {
                Copy-Item -Path $sourcePath -Destination $backupPath -Force | Out-Null
            }
        }

        Write-UpdateLog "✓ Backup created at: $backupPath" "INFO"
        return $backupPath
    }
    catch {
        Write-UpdateLog "Backup failed: $($_.Exception.Message)" "ERROR"
        return $null
    }
}

function Install-Update {
    param(
        [string]$InstallerPath,
        [string]$BackupPath
    )

    try {
        Write-UpdateLog "Starting installation..." "INFO"

        # Run the installer silently
        # The installer itself (NSIS) will handle the actual update
        Write-UpdateLog "Running installer: $InstallerPath" "INFO"

        $process = Start-Process -FilePath $InstallerPath `
            -ArgumentList "/S /D=`"$InstallDir`"" `
            -NoNewWindow `
            -PassThru `
            -Wait

        if ($process.ExitCode -eq 0) {
            Write-UpdateLog "✓ Installation completed successfully" "INFO"
            return $true
        }
        else {
            Write-UpdateLog "Installation returned exit code: $($process.ExitCode)" "ERROR"
            return $false
        }
    }
    catch {
        Write-UpdateLog "Installation failed: $($_.Exception.Message)" "ERROR"

        if ($BackupPath -and (Test-Path $BackupPath)) {
            Write-UpdateLog "Attempting to restore from backup..." "WARN"
            Restore-FromBackup $BackupPath
        }

        return $false
    }
}

function Restore-FromBackup {
    param(
        [string]$BackupPath
    )

    try {
        Write-UpdateLog "Restoring from backup: $BackupPath" "INFO"

        Get-ChildItem -Path $BackupPath -File | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $InstallDir -Force
        }

        Write-UpdateLog "✓ Restored from backup" "INFO"
        return $true
    }
    catch {
        Write-UpdateLog "Restore failed: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Cleanup-OldUpdates {
    try {
        Write-UpdateLog "Cleaning up old update files..." "INFO"

        # Remove installer files older than 7 days
        $cutoffDate = (Get-Date).AddDays(-7)

        Get-ChildItem -Path $updateDir -File | Where-Object {
            $_.LastWriteTime -lt $cutoffDate
        } | ForEach-Object {
            Remove-Item -Path $_.FullName -Force
            Write-UpdateLog "Removed old update: $($_.Name)" "INFO"
        }

        # Remove backups older than 30 days
        $cutoffDate = (Get-Date).AddDays(-30)
        Get-ChildItem -Path $backupDir -Directory | Where-Object {
            $_.LastWriteTime -lt $cutoffDate
        } | ForEach-Object {
            Remove-Item -Path $_.FullName -Recurse -Force
            Write-UpdateLog "Removed old backup: $($_.Name)" "INFO"
        }
    }
    catch {
        Write-UpdateLog "Cleanup encountered an error: $($_.Exception.Message)" "WARN"
    }
}

# ============================================================================
# Main Logic
# ============================================================================

function Main {
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║           Launcher Update Installation                     ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    Write-UpdateLog "Starting update process..." "INFO"
    Write-UpdateLog "Install directory: $InstallDir" "INFO"

    # Create necessary directories
    New-UpdateDirectory

    # Download the update
    $installerFileName = Split-Path -Leaf $DownloadUrl
    $installerPath = Join-Path -Path $updateDir -ChildPath $installerFileName

    if (-not (Download-Update -Url $DownloadUrl -OutputPath $installerPath)) {
        Write-UpdateLog "Failed to download update" "ERROR"
        return $false
    }

    # Verify file integrity
    if (-not (Test-FileIntegrity -FilePath $installerPath -ExpectedChecksum $Checksum)) {
        Write-UpdateLog "File integrity check failed" "ERROR"
        Remove-Item -Path $installerPath -Force -ErrorAction SilentlyContinue
        return $false
    }

    # Create backup
    $backupPath = Backup-CurrentVersion

    # Install the update
    if (-not (Install-Update -InstallerPath $installerPath -BackupPath $backupPath)) {
        Write-UpdateLog "Installation failed" "ERROR"
        return $false
    }

    # Cleanup
    Cleanup-OldUpdates

    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║          Launcher Update Installed Successfully            ║" -ForegroundColor Green
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""

    Write-UpdateLog "Update installation completed" "INFO"

    return $true
}

# Run main logic
$success = Main
exit $(if ($success) { 0 } else { 1 })
