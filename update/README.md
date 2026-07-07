# Launcher Auto-Update System Documentation

## Overview

The Launcher update system checks a GitHub-hosted manifest, compares versions, and then downloads the published installer from GitHub Releases.

The main pieces are:

- `Check-LauncherUpdate.ps1` detects available updates.
- `Install-LauncherUpdate.ps1` downloads and installs updates.
- `versions.json` tracks available releases.
- `Update-Integration.ps1` shows how to wire updates into `launcher.ps1`.

## Flow

```text
GitHub raw versions.json
        ↓
Check-LauncherUpdate.ps1
        ↓ if newer version exists
User prompt
        ↓ if accepted
Install-LauncherUpdate.ps1
        ↓
GitHub Releases installer download
```

## Setup

### Host the manifest

Use the GitHub raw URL for the manifest:

```text
https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json
```

If you prefer your own server, keep the JSON structure the same and update `updateCheckUrl` in the launcher config.

### Update the config

```json
{
  "checkForUpdates": true,
  "updateCheckUrl": "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json"
}
```

### Update the launcher

The launcher now loads the update helper from `update/Update-Integration.ps1` and starts a silent update check at startup when `checkForUpdates` is enabled.

## Manifest Format

```json
{
  "version": "1.0.4",
  "releaseDate": "2026-07-07",
  "description": "Patch release",
  "downloadUrl": "https://github.com/Irish-Coder69/Launcher/releases/download/v1.0.4/Launcher-1.0.4-Setup.exe",
  "checksum": "SHA256-HERE",
  "isRequired": true,
  "notes": [
    "Refreshed installer build",
    "Updated documentation"
  ]
}
```

## Manual Usage

### Check for updates

```powershell
& ".\update\Check-LauncherUpdate.ps1" `
  -InstallDir "C:\Program Files\Launcher" `
  -VersionsUrl "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json"
```

### Silent check

```powershell
$result = & ".\update\Check-LauncherUpdate.ps1" `
  -InstallDir "C:\Program Files\Launcher" `
  -VersionsUrl "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json" `
  -Silent

if ($result.Available) {
  Write-Host "Update available: $($result.Latest)"
}
```

### Install manually

```powershell
& ".\update\Install-LauncherUpdate.ps1" `
  -DownloadUrl "https://github.com/Irish-Coder69/Launcher/releases/download/v1.0.4/Launcher-1.0.4-Setup.exe" `
  -InstallDir "C:\Program Files\Launcher" `
  -Checksum "0B16FF9B5FC8CA63FF9670A7166498C2512F1E610A24A92B085B852A9D89C1A6"
```

## Notes

- The manifest and installer download should both be public if you want users to update without special access.
- Keep SHA-256 checksums in sync with the published installer.
- If you change the release tag or filename, update both `versions.json` and the GitHub Release asset name.

## Next Steps

1. **Choose hosting location** for versions.json (GitHub, Azure, etc.)
2. **Update configuration files** with your URLs
3. **Test update checking** manually
4. **Test installation** with test version
5. **Monitor backups** and cleanup old files periodically

## References

- [Semantic Versioning](https://semver.org/)
- [NSIS Installer](https://nsis.sourceforge.io/)
- [PowerShell Web Requests](https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/invoke-webrequest)
