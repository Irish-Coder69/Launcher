# Update Checker for Launcher
# Checks for available updates and notifies the user
# Designed to be called from launcher.ps1 or run standalone

[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification='Interactive update prompt intentionally uses host colors.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification='Top-level script parameters are consumed in script scope.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification='Function names describe collection-based operations in context.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseApprovedVerbs', '', Justification='Legacy function names kept stable for maintainability.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseBOMForUnicodeEncodedFile', '', Justification='Repository standardizes on UTF-8 without BOM unless required.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseCatchBlockForTypeConversion', '', Justification='Empty catch intentionally allows fallback to next URL.')]
param(
    [string]$InstallDir = $PSScriptRoot,
    [string]$VersionsUrl = "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json",
    [switch]$CheckOnly,
    [switch]$Silent,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Continue"
Set-StrictMode -Version Latest

# ============================================================================
# Configuration
# ============================================================================

$versionFile = Join-Path -Path $InstallDir -ChildPath "version.txt"
$lastCheckFile = Join-Path -Path $env:APPDATA -ChildPath "Launcher\last-update-check.txt"
# ============================================================================
# Functions
# ============================================================================

function Write-UpdateLog {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )

    if (-not $Silent) {
        $timestamp = Get-Date -Format "HH:mm:ss"
        Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $(
            if ($Level -eq "ERROR") { "Red" }
            elseif ($Level -eq "WARN") { "Yellow" }
            else { "Green" }
        )
    }
}

function Get-InstalledVersion {
    if (Test-Path $versionFile) {
        $version = Get-Content $versionFile -Raw | ForEach-Object { $_.Trim() }
        return $version
    }
    return "0.0.0"
}

function Compare-Versions {
    param(
        [string]$Current,
        [string]$Available
    )

    # Split versions into parts
    $currentParts = $Current.Split(".") | ForEach-Object { [int]$_ }
    $availableParts = $Available.Split(".") | ForEach-Object { [int]$_ }

    # Pad with zeros if needed
    while ($currentParts.Count -lt $availableParts.Count) {
        $currentParts += 0
    }
    while ($availableParts.Count -lt $currentParts.Count) {
        $availableParts += 0
    }

    # Compare each part
    for ($i = 0; $i -lt $currentParts.Count; $i++) {
        if ($availableParts[$i] -gt $currentParts[$i]) {
            return -1  # Available is newer
        }
        elseif ($availableParts[$i] -lt $currentParts[$i]) {
            return 1   # Current is newer
        }
    }

    return 0  # Same version
}

function Test-InternetConnection {
    $urls = @(
        "https://www.google.com",
        "https://www.cloudflare.com",
        "https://www.microsoft.com"
    )

    foreach ($url in $urls) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                return $true
            }
        }
        catch {
            # Try next URL on any failure
            $null = $_
        }
    }

    return $false
}

function Get-AvailableVersions {
    param(
        [string]$Url
    )

    try {
        Write-UpdateLog "Connecting to update server: $Url" "INFO"

        $response = Invoke-WebRequest -Uri $Url `
            -UseBasicParsing `
            -TimeoutSec $TimeoutSeconds `
            -ErrorAction Stop

        $versions = $response.Content | ConvertFrom-Json

        if ($versions -is [array] -and $versions.Count -gt 0) {
            return $versions | Sort-Object { [Version]$_.version } -Descending
        }

        return $null
    }
    catch {
        Write-UpdateLog "Failed to fetch versions: $($_.Exception.Message)" "WARN"
        return $null
    }
}

function Show-UpdatePrompt {
    param(
        [PSObject]$CurrentVersion,
        [PSObject]$NewVersion
    )

    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║              Launcher Update Available                     ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Current Version: $CurrentVersion" -ForegroundColor Gray
    Write-Host "  New Version:     $($NewVersion.version)" -ForegroundColor Green
    Write-Host "  Release Date:    $($NewVersion.releaseDate)" -ForegroundColor Gray
    Write-Host ""

    if ($NewVersion.notes -and $NewVersion.notes.Count -gt 0) {
        Write-Host "  What's New:" -ForegroundColor Cyan
        foreach ($note in $NewVersion.notes) {
            Write-Host "    • $note" -ForegroundColor Gray
        }
        Write-Host ""
    }

    $choice = Read-Host "  Update now? [Y]es / [N]o / [R]emind me later"

    return $choice
}

function Save-LastCheckTime {
    try {
        $checkDir = Split-Path -Parent $lastCheckFile
        if (-not (Test-Path $checkDir)) {
            New-Item -ItemType Directory -Path $checkDir -Force | Out-Null
        }

        Get-Date -Format "yyyy-MM-dd HH:mm:ss" | Set-Content -Path $lastCheckFile -Force
    }
    catch {
        Write-UpdateLog "Could not save last check time: $($_.Exception.Message)" "WARN"
    }
}

# ============================================================================
# Main Logic
# ============================================================================

function Main {
    if (-not $Silent) {
        Write-UpdateLog "Starting update check..." "INFO"
    }

    # Verify installation directory
    if (-not (Test-Path $InstallDir)) {
        Write-UpdateLog "Installation directory not found: $InstallDir" "ERROR"
        return @{ Available = $false; Reason = "Install directory not found" }
    }

    # Get current version
    $currentVersion = Get-InstalledVersion

    if (-not $Silent) {
        Write-UpdateLog "Current installed version: $currentVersion" "INFO"
    }

    # Check internet connectivity
    if (-not (Test-InternetConnection)) {
        Write-UpdateLog "No internet connection available" "WARN"
        return @{ Available = $false; Reason = "No internet" }
    }

    # Check for available versions
    $versions = Get-AvailableVersions -Url $VersionsUrl

    if (-not $versions) {
        Write-UpdateLog "Could not retrieve version information" "WARN"
        return @{ Available = $false; Reason = "Could not fetch versions" }
    }

    # Find the latest version
    $latestVersion = $versions[0]

    if (-not $latestVersion) {
        Write-UpdateLog "No version information available" "WARN"
        return @{ Available = $false; Reason = "No versions available" }
    }

    # Compare versions
    $comparison = Compare-Versions $currentVersion $latestVersion.version

    if ($comparison -lt 0) {
        # Newer version available
        if (-not $Silent) {
            Write-UpdateLog "Update available: $($latestVersion.version)" "INFO"
        }

        if (-not $CheckOnly) {
            $userChoice = Show-UpdatePrompt $currentVersion $latestVersion

            Save-LastCheckTime

            return @{
                Available = $true
                Current = $currentVersion
                Latest = $latestVersion.version
                Details = $latestVersion
                UserChoice = $userChoice
            }
        }

        Save-LastCheckTime
        return @{ Available = $true; Latest = $latestVersion.version }
    }
    else {
        if (-not $Silent) {
            Write-UpdateLog "Launcher is up to date" "INFO"
        }

        Save-LastCheckTime
        return @{ Available = $false; Reason = "Already up to date" }
    }
}

# Run main logic
$result = Main
return $result
