# Launcher Native (Phase 1)

This folder contains the first native desktop migration for Launcher.

## Current Scope

- WPF desktop app shell (`Launcher.App`)
- Core services/models (`Launcher.Core`)
- Bridge execution of existing `launcher.ps1` modes:
  - `Start`
  - `Close`
  - `StartAndWaitForCloseCommand`
- Native config editing and save workflow for global settings and step enabled states
- Native step detection service for running-state and close-target discovery

## What Works Today

- Load and preview steps from `launcher.config.json`
- Run `Start` and `Close` from native UI
- Live output panel for non-interactive runs
- Open an interactive PowerShell window for `StartAndWaitForCloseCommand`
- Edit and save these settings back to `launcher.config.json`:
  - `checkForUpdates`
  - `ensureCapsLockOn`
  - `ensureNumLockOn`
  - `closeOptions.*` defaults
  - each step `enabled` state
- Test first native logic port from UI:
  - `Native Detect Running`
  - `Native Detect Close Targets`

## Build

```powershell
Set-Location .\native
dotnet build .\Launcher.Native.slnx
```

## Run

```powershell
Set-Location .\native\Launcher.App
dotnet run
```

The app auto-detects `launcher.ps1` and `launcher.config.json` by walking up parent folders from the app runtime directory.

## Publish

### PowerShell Script

```powershell
Set-Location .\native
.\Publish-NativeApp.ps1
```

### Direct `dotnet publish`

```powershell
Set-Location .\native
dotnet publish .\Launcher.App\Launcher.App.csproj -c Release -p:PublishProfile=LauncherApp-win-x64
```

Output folder:

- `native\publish\win-x64\`

## Next Migration Steps

1. Move run-state/session tracking from script into `Launcher.Core`.
2. Expand native detection from discovery to full native open/close execution.
3. Move login automation flows into native code.
4. Keep script bridge as fallback until feature parity is complete.
