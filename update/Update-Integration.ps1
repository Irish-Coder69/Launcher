# Helper functions for integrating update checking into launcher.ps1
# Copy the relevant functions from this file into launcher.ps1 to enable auto-update support

# ============================================================================
# Update Integration Functions for launcher.ps1
# ============================================================================

# Add this function to launcher.ps1 to check for updates at startup

function Invoke-UpdateCheck {
    <#
    .SYNOPSIS
    Checks for Launcher updates and prompts the user to install if available.

    .DESCRIPTION
    This function should be called at the beginning of launcher.ps1 after
    configuration loading. It will check for updates without blocking the
    launcher startup (with option to make it blocking).

    .PARAMETER Block
    If $true, waits for update check to complete before continuing.
    If $false (default), runs check asynchronously.

    .PARAMETER VersionsUrl
    URL to the versions.json file containing available releases.

    .PARAMETER UpdateScriptPath
    Path to Check-LauncherUpdate.ps1 script.
    #>

    param(
        [string]$InstallDir = $PSScriptRoot,
        [string]$VersionsUrl = "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json",
        [string]$UpdateScriptPath = (Join-Path $PSScriptRoot "update\Check-LauncherUpdate.ps1"),
        [bool]$Block = $false,
        [bool]$Silent = $false
    )

    if (-not (Test-Path $UpdateScriptPath)) {
        Write-LauncherLog "Update script not found: $UpdateScriptPath" "WARN"
        return
    }

    if ($Block) {
        # Run update check synchronously
        $result = & $UpdateScriptPath -InstallDir $InstallDir -VersionsUrl $VersionsUrl -Silent:$Silent
        if ($result.Available -eq $true -and $result.UserChoice -match "^Y") {
            $installScript = Join-Path (Split-Path $UpdateScriptPath) "Install-LauncherUpdate.ps1"
            if (Test-Path $installScript) {
                & $installScript -DownloadUrl $result.Details.downloadUrl -InstallDir $InstallDir -Checksum $result.Details.checksum
            }
        }
    }
    else {
        # Run update check in background job
        Start-Job -ScriptBlock {
            $result = & $using:UpdateScriptPath -InstallDir $using:InstallDir -VersionsUrl $using:VersionsUrl -Silent:$using:Silent
            if ($result.Available -eq $true -and $result.UserChoice -match "^Y") {
                $installScript = Join-Path (Split-Path $using:UpdateScriptPath) "Install-LauncherUpdate.ps1"
                if (Test-Path $installScript) {
                    & $installScript -DownloadUrl $result.Details.downloadUrl -InstallDir $using:InstallDir -Checksum $result.Details.checksum
                }
            }
        } | Out-Null
    }
}

# ============================================================================
# Code to add to launcher.ps1
# ============================================================================

<#
Add the following near the beginning of launcher.ps1 (after loading config):

# Check for updates if enabled in config
if ($config.checkForUpdates -ne $false) {
    Write-LauncherLog "Checking for Launcher updates..." "INFO"
    Invoke-UpdateCheck `
        -InstallDir (Split-Path $PSScriptRoot) `
        -Block:$false `
        -Silent:$true
}

And add this to your launcher.config.json:

{
  "checkForUpdates": true,
    "updateCheckUrl": "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json",
  ...
}

#>
