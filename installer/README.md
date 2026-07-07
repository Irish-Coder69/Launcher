# Launcher Installer Setup

This directory contains the build files for creating a Windows installer for the Launcher application.

## Prerequisites

Before building the installer, install **NSIS** (Nullsoft Scriptable Install System).

1. Download NSIS from [NSIS Download](https://nsis.sourceforge.io/Download)
2. Install using the default settings
3. Verify installation by running `makensis.exe /version`

## Building the Installer

### Method 1: PowerShell Script (Recommended)

```powershell
# From this directory
.\Build-Installer.ps1

# Or with options
.\Build-Installer.ps1 -Force -OpenAfterBuild
```

Options:

- `-Force` - Rebuild even if an installer already exists
- `-OpenAfterBuild` - Automatically run the generated installer

### Method 2: Manual NSIS Compilation

```powershell
makensis.exe /DOUTDIR="Output" Launcher.nsi
```

## Output

The compiled installer is created in the `Output` subfolder:

- `Launcher-1.0.5-Setup.exe` - The Windows installer

## File Structure

```text
installer/
├── Launcher.nsi           # NSIS installer script
├── LICENSE.txt            # License agreement shown during install
├── Build-Installer.ps1    # PowerShell build script
├── README.md              # This file
└── Output/                # Generated installers (created after build)
   └── Launcher-1.0.5-Setup.exe
```

## What the Installer Does

The installer:

1. **Installs application files**
   - Copies `launcher.ps1` and all configuration files to the installation directory
   - Creates a batch wrapper (`launcher.bat`) for easy launching
   - Stores the version number for auto-update checks

2. **Creates shortcuts**
   - **Start Menu**: Main launcher and uninstaller
   - **Desktop**: Main launcher shortcut, if selected during install

3. **Allows custom installation location**
   - Users can choose where to install, with a default of `C:\Program Files\Launcher`
   - Requires administrator privileges for `Program Files`
   - Can be installed to AppData for non-admin users

4. **Registers with Windows**
   - Appears in "Add or Remove Programs"
   - Includes full uninstaller support

## Installation Directory Structure

After installation, the user has:

```text
Program Files/Launcher/
├── launcher.ps1
├── launcher.bat            # Batch wrapper
├── launcher.config.json
├── version.txt             # For update checks
└── Uninstall.exe
```

## Shortcut Targets

The generated shortcuts execute:

- **Default**: `launcher.bat` (uses `launcher.config.json`)

## Uninstalling

Users can uninstall via:

1. "Add or Remove Programs" in Windows Settings
2. The uninstall shortcut in the Start Menu
3. `Uninstall.exe` in the installation directory

## Troubleshooting

### NSIS is not installed

- Download and install NSIS from [NSIS Download](https://nsis.sourceforge.io/Download)
- Ensure `makensis.exe` is in your PATH or in `C:\Program Files (x86)\NSIS\`

### Build log shows errors

- Check that all source files exist in the parent directory:
  - `launcher.ps1`
  - `launcher.config.json`
  - `launcher.normal-slow.config.json`
  - `launcher.ultra-slow.config.json`

### Installer won't run on target machine

- Ensure the target is Windows 7 or later
- Check that PowerShell execution policies allow running `launcher.ps1`
- Run the generated shortcut as administrator if needed

## Customization

To customize the installer:

1. Change the version by editing `PRODUCT_VERSION` in `Launcher.nsi`
2. Change the install directory by editing `InstallDir` in `Launcher.nsi`
3. Add files with `File` commands in the `SEC01` section
4. Change the license by replacing `LICENSE.txt`
5. Modify shortcuts in the `SEC02` and `SEC03` sections

## Next Steps

See the parent directory's README for:

- Auto-update mechanism setup
- Running the launcher from shortcuts
- Configuration management
