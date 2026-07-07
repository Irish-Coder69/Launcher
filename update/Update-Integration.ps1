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
        [string]$UpdateScriptPath = "",
        [bool]$Block = $false,
        [bool]$Silent = $false,
        [bool]$InstallBeforeContinue = $false,
        [bool]$RequireAnyUpdate = $false
    )

    if ([string]::IsNullOrWhiteSpace($UpdateScriptPath)) {
        $scriptRoot = $PSScriptRoot
        if ((Split-Path -Path $scriptRoot -Leaf).ToLowerInvariant() -eq "update") {
            $scriptRoot = Split-Path -Path $scriptRoot -Parent
        }

        $UpdateScriptPath = Join-Path $scriptRoot "update\Check-LauncherUpdate.ps1"
    }

    if (-not (Test-Path $UpdateScriptPath)) {
        Write-LauncherLog "Update script not found: $UpdateScriptPath" "WARN"
        return
    }

    if ($Block) {
        if ($InstallBeforeContinue) {
            # Enforced path: check silently, then install automatically when required.
            $result = & $UpdateScriptPath -InstallDir $InstallDir -VersionsUrl $VersionsUrl -CheckOnly -Silent:$true
            $isRequiredByManifest = $false
            if ($result -and $result.Details -and $result.Details.PSObject.Properties.Name -contains "isRequired") {
                $isRequiredByManifest = [bool]$result.Details.isRequired
            }

            $shouldInstall = ($result.Available -eq $true) -and ($RequireAnyUpdate -or $isRequiredByManifest)
            if ($shouldInstall) {
                $installScript = Join-Path (Split-Path $UpdateScriptPath) "Install-LauncherUpdate.ps1"
                if (-not (Test-Path $installScript)) {
                    throw "Update installer script not found: $installScript"
                }

                if (-not $result.Details -or [string]::IsNullOrWhiteSpace([string]$result.Details.downloadUrl)) {
                    throw "Update is available but downloadUrl is missing from the manifest."
                }

                & $installScript -DownloadUrl $result.Details.downloadUrl -InstallDir $InstallDir -Checksum $result.Details.checksum
                return @{
                    Available = $true
                    UpdateInstalled = $true
                    Latest = $result.Latest
                    Details = $result.Details
                }
            }

            return @{
                Available = [bool]$result.Available
                UpdateInstalled = $false
                Latest = $result.Latest
                Details = $result.Details
            }
        }

        # Run update check synchronously with user prompt flow.
        $result = & $UpdateScriptPath -InstallDir $InstallDir -VersionsUrl $VersionsUrl -Silent:$Silent
        if ($result.Available -eq $true -and $result.UserChoice -match "^Y") {
            $installScript = Join-Path (Split-Path $UpdateScriptPath) "Install-LauncherUpdate.ps1"
            if (Test-Path $installScript) {
                & $installScript -DownloadUrl $result.Details.downloadUrl -InstallDir $InstallDir -Checksum $result.Details.checksum
            }
        }

        return $result
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

        return $null
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
