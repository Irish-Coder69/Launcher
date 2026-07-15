# Launcher - Complete Installation & Distribution Package

## What You Now Have

A complete, professional Windows application installer with auto-update capability. Here is the directory structure:

```text
Launcher/
├── launcher.ps1                     # Main launcher application
├── launcher.config.json             # Default configuration
├── launcher.normal-slow.config.json # Normal-slow profile
├── launcher.ultra-slow.config.json  # Ultra-slow profile
│
├── installer/                       # Installer creation tools
│   ├── Launcher.nsi                 # NSIS installer script
│   ├── LICENSE.txt                  # License agreement
│   ├── Build-Installer.ps1          # Build script
│   ├── Output/                      # Generated installers (after build)
│   │   └── Launcher-1.0.0-Setup.exe
│   └── README.md                    # Installer documentation
│
└── update/                          # Auto-update system
    ├── Check-LauncherUpdate.ps1     # Update checker
    ├── Install-LauncherUpdate.ps1   # Update installer
    ├── Update-Integration.ps1       # Integration helper
    ├── versions.json                # Available versions manifest
    └── README.md                    # Auto-update documentation
```

## Quick Start

### Phase 1: Build the Installer (1-2 minutes)

#### Prerequisites

- Windows 7 or later
- PowerShell 5.0 or later
- NSIS installed

**Install NSIS:**

1. Download from [NSIS Download](https://nsis.sourceforge.io/Download)
2. Run installer with default settings
3. Restart terminal after installation

**Build the installer:**

```powershell
cd ".\installer"
.\Build-Installer.ps1 -Force -OpenAfterBuild
```

This creates `Launcher-1.0.0-Setup.exe` in the `Output` folder.

### Phase 2: Test Local Installation (5 minutes)

```powershell
# Run the installer
$installerPath = ".\installer\Output\Launcher-1.0.0-Setup.exe"
& $installerPath

# Follow the wizard:
# 1. Accept license
# 2. Choose components (all selected by default)
# 3. Choose install location (default: C:\Program Files\Launcher)
# 4. Click Install
```

**After installation:**

- Check Start Menu for the Launcher folder
- Check Desktop for the Launcher shortcut
- Click the shortcut to verify it works

### Phase 3: Set Up Auto-Updates (10 minutes)

#### Step 1: Host versions.json

Choose one hosting option:

#### Option A: GitHub Gist (Recommended for testing)

1. Go to [GitHub Gist](https://gist.github.com)
2. Create a new gist with `versions.json` content
3. Make it public
4. Copy the raw content URL

#### Option B: Your Web Server

- Upload `versions.json` to your server
- Ensure it is accessible via HTTPS

#### Step 2: Update Configuration

Edit `launcher.config.json`:

```json
{
  "checkForUpdates": true,
    "updateCheckUrl": "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json",
  "...rest of config": true
}
```

Or use any profile name (`normal-slow`, `ultra-slow`).

For launch steps that point at mapped drives or network shortcuts, you can also add an optional `fallbackProgramPath` with a UNC path. The launcher will try `programPath` first and then the fallback if the primary path is unavailable.

#### Step 3: Update Installer (NSIS)

Edit `installer/Launcher.nsi` around line 95:

```nsis
; Add these lines in the SEC01 section:
File "..\update\Check-LauncherUpdate.ps1"
File "..\update\Install-LauncherUpdate.ps1"
```

Rebuild the installer:

```powershell
.\Build-Installer.ps1 -Force
```

#### Step 4: Test Update Check

```powershell
# Manually check for updates
& ".\update\Check-LauncherUpdate.ps1" `
    -InstallDir "C:\Program Files\Launcher" `
    -VersionsUrl "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json"
```

## Creating a New Release

When you have updates to release:

### Step 1: Update Version Number

Edit `launcher.ps1` or config to reflect the new version:

```powershell
$Version = "1.1.0" # Update version
```

Edit `installer/Launcher.nsi`:

```nsis
!define PRODUCT_VERSION "1.1.0"
```

### Step 2: Rebuild Installer

```powershell
cd ".\installer"
.\Build-Installer.ps1 -Force
```

### Step 3: Upload Installer

- Upload `Launcher-1.1.0-Setup.exe` to your release hosting
- Generate the checksum:

```powershell
(Get-FileHash "Launcher-1.1.0-Setup.exe" -Algorithm SHA256).Hash
```

### Step 4: Update versions.json

```json
[
  {
    "version": "1.1.0",
    "releaseDate": "2026-06-20",
    "description": "New features and bug fixes",
    "downloadUrl": "https://your-server.com/releases/Launcher-1.1.0-Setup.exe",
    "checksum": "PASTE_SHA256_HERE",
    "isRequired": false,
    "notes": [
      "Fixed issue with login sequence",
      "Added support for new applications",
      "Improved error logging"
    ]
  },
  {
    "version": "1.0.0",
    "releaseDate": "2026-06-17",
    "description": "Initial release"
  }
]
```

### Step 5: Upload versions.json

Upload the updated `versions.json` to your hosting location.

**Users will now see the update when:**

- They run Launcher if `checkForUpdates` is enabled
- They manually run `Check-LauncherUpdate.ps1`

## Distribution

### Option 1: Direct Distribution

- Email users the `Launcher-1.0.0-Setup.exe` file
- They double-click to install

### Option 2: Self-Service Portal

1. Create a webpage with a download link
2. Include release notes and version history
3. Link to `versions.json` for auto-update support

### Option 3: Internal Repository

- Host on internal server
- Point to `versions.json` for updates
- Create documentation for installation

## File Reference

| Component | Purpose | Edit When |
| --- | --- | --- |
| `launcher.ps1` | Main application | Adding features |
| `launcher.*.config.json` | Configuration | Changing app startup behavior |
| `installer/Launcher.nsi` | Installer script | Changing install process |
| `update/versions.json` | Release manifest | Publishing a new version |
| `LICENSE.txt` | Legal terms | Updating license |

## Troubleshooting

**Installer won't build:**

- Ensure NSIS is installed
- Run `makensis.exe /version` to verify
- Check file paths in `Launcher.nsi`

**Installation fails:**

- Run as administrator
- Check disk space
- Ensure no conflicting processes

**Update check doesn't work:**

- Verify internet connection
- Check URL in config
- Ensure `versions.json` is valid JSON
- Run `Check-LauncherUpdate.ps1` with verbose output

## Checklist for Release

- [ ] Version number updated in `Launcher.nsi`
- [ ] Installer built successfully
- [ ] Tested on a clean Windows system
- [ ] Installer creates Start Menu shortcuts
- [ ] Desktop shortcut created, if selected
- [ ] Uninstaller works
- [ ] Launcher runs from installed location
- [ ] Update check script tested, if using auto-update
- [ ] Checksum calculated for new version
- [ ] `versions.json` updated
- [ ] Release notes documented
- [ ] Users notified of new version

## Next Steps

1. Install NSIS if it is not already installed
2. Build and test the installer locally
3. Choose hosting for `versions.json`
4. Set up update configuration
5. Test update checking with a test version
6. Distribute to users

## Detailed Documentation

- Installer details: [installer/README.md](installer/README.md)
- Auto-update system: [update/README.md](update/README.md)
- Configuration guide: See within `launcher.ps1`

## Tips

- Keep backups of older versions in `versions.json`
- Test updates on non-production machines first
- Use semantic versioning: major.minor.patch
- Document breaking changes in release notes
- Monitor user feedback after releases

## Success Criteria

You can now:

- Build professional Windows installers
- Distribute the Launcher application
- Check for and install updates automatically
- Manage multiple versions
- Provide proper uninstall capability

---

Questions? Refer to the detailed README files in the `installer/` and `update/` directories.
