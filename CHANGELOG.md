# Changelog

## 1.0.29 - 2026-07-16

- Renamed the lock-key helper to use an approved PowerShell verb.
- Kept the lock-key enforcement logic and call flow unchanged.
- Rebuilt installer and refreshed release metadata for v1.0.29.

## 1.0.28 - 2026-07-16

- Fixed Access app detection so Visual Board and Stockroom Analytics are checked as separate programs.
- Removed shared open-state signals that could cause one Access app to suppress the other.
- Rebuilt installer and refreshed release metadata for v1.0.28.

## 1.0.27 - 2026-07-16

- Fixed lock-key enforcement fallback binding so non-boolean values no longer trigger parameter conversion errors.
- Preserved non-fatal lock-key behavior so launcher completion is not blocked by key-state fallback issues.

## 1.0.26 - 2026-07-16

- Added explicit Explorer folder-window detection so the Receiver's directory step is recognized as already open.
- Made lock-key toggling warning-only on send failure so key-toggle issues no longer stop the launcher run.
- Rebuilt installer and refreshed release metadata for v1.0.26.

## 1.0.25 - 2026-07-16

- Improved already-open detection so launch steps use broader window-title matching and always run process fallback checks.
- Added Access process fallback detection (`MSACCESS`) for `.accdb` / `.accde` launch targets.
- Rebuilt installer and refreshed release metadata for v1.0.25.

## 1.0.24 - 2026-07-16

- Added launch-step detection so reruns skip apps that are already open and only launch missing steps.
- Added per-step override support (`launchOnlyIfMissing`, `runningWindowTitles`, `runningProcessNames`).
- Rebuilt installer and refreshed release metadata for v1.0.24.

## 1.0.23 - 2026-07-16

- Restored the idle CLOSE command prompt so the launcher can be closed from the terminal.
- Hardened Caps Lock and Num Lock enforcement with verification and retries to ensure ON state.
- Rebuilt installer and refreshed release metadata for v1.0.23.

## 1.0.22 - 2026-07-16

- Fixed the Visual Board pre-login maximize behavior so it targets the main program window instead of the login form.
- Rebuilt installer and refreshed release metadata for v1.0.22.

## 1.0.21 - 2026-07-16

- Restored the interactive update prompt flow so Launcher asks before downloading and installing an update.
- Disabled required pre-run update mode by default in the launcher configuration.
- Rebuilt installer and refreshed release metadata for v1.0.21.

## 1.0.20 - 2026-07-16

- Launcher now stays open in a passive idle state after all configured steps finish instead of prompting for CLOSE.
- Kept the lock-key and update integration fixes from the same release train.
- Rebuilt installer and refreshed release metadata for v1.0.20.

## 1.0.19 - 2026-07-16

- Launcher now exits automatically after all configured steps finish opening instead of waiting for the CLOSE prompt.
- Kept the lock-key and update integration fixes from the same release train.
- Rebuilt installer and refreshed release metadata for v1.0.19.

## 1.0.18 - 2026-07-15

- Fixed Stockroom Analytics startup path to launch the real `.accdb` target instead of a stale shortcut path.
- Corrected UNC fallback path escaping in configuration so network fallback resolves reliably at runtime.
- Rebuilt installer and refreshed release metadata for v1.0.18.

## 1.0.17 - 2026-07-15

- Added resilient launch path resolution for mapped/network targets, including UNC fallback support via `fallbackProgramPath`.
- Hardened updater and installer flow (quoted silent install path, Launcher.cmd backup parity, and GitHub-focused connectivity checks).
- Fixed NSIS install flow branches and updated installer build/documentation to emit and reference only the current-version installer.

## 1.0.16 - 2026-07-13

- Fixed P-touch Editor 5.4 post-launch minimization reliability when the visible window is owned by a different process handle.
- Added process-name and timed title retry fallbacks to launch-step window minimization logic.
- Rebuilt installer and refreshed release metadata for v1.0.16.

## 1.0.15 - 2026-07-10

- Added a single-instance launcher lock to prevent duplicate concurrent runs.
- Added Outlook launch guard to skip starting Outlook when it is already running.
- Rebuilt installer and refreshed release metadata for v1.0.15.

## 1.0.14 - 2026-07-10

- Removed the Local Visual Board Add/Update Table automation step from startup flow.
- Visual Board now proceeds directly after login without table-update password/update wait actions.
- Rebuilt installer and refreshed release metadata for v1.0.14.

## 1.0.13 - 2026-07-10

- Updated Visual Board update-table button candidate labels to the Add/Update naming.
- Kept the post-login update-table automation sequence and timing for stable execution.
- Rebuilt installer and refreshed release metadata for v1.0.13.

## 1.0.12 - 2026-07-10

- Added full descendant UI Automation fallback for Update Table control discovery in Visual Board.
- Improved resilience when Access exposes clickable controls with non-standard control types.
- Rebuilt installer and refreshed release metadata for v1.0.12.

## 1.0.11 - 2026-07-10

- Hardened Visual Board update-table automation to detect more Access UI control types.
- Added broader Update Table button matching and increased post-login settle wait before update click.
- Rebuilt installer and refreshed release metadata for v1.0.11.

## 1.0.10 - 2026-07-10

- Restored Visual Board Add / UPDATE TABLE automation after login before Stockroom Analytics starts.
- Added password submission and a 3-minute wait for the table update process to finish.
- Rebuilt installer and refreshed release metadata for v1.0.10.

## 1.0.9 - 2026-07-07

- Fixed startup update integration path so launcher checks GitHub updates correctly from installed path.
- Rebuilt installer and refreshed release metadata for v1.0.9.

## 1.0.8 - 2026-07-07

- Removed trailing whitespace flagged by PSScriptAnalyzer in launcher script.
- Rebuilt installer and refreshed release metadata for v1.0.8.

## 1.0.7 - 2026-07-07

- Added visible launcher version in the window title and startup banner.
- Added startup log line showing the running launcher version.
- Rebuilt installer and release metadata for v1.0.7.

## 1.0.6 - 2026-07-07

- Updated P-touch step to maximize then minimize once (removed duplicate minimize).
- Rebuilt installer and release metadata for v1.0.6.

## 1.0.5 - 2026-07-07

- Improved update manifest fetch reliability by using GitHub Contents API for GitHub raw manifest URLs.
- Ensured update checks always resolve the latest release metadata before startup enforcement.

## 1.0.4 - 2026-07-07

- Added required update-before-run mode so Launcher can install newer versions before step execution.
- Added pre-login Visual Board and Stockroom window handling improvements.
- Rebuilt installer and update manifest metadata for v1.0.4.

## 1.0.3 - 2026-07-07

- Rebuilt the installer for the latest release package.
- Updated release metadata and documentation to reference v1.0.3 assets.

## 1.0.2 - 2026-07-02

- Rebuilt the installer for the moved workspace location.
- Cleaned up installer and launch documentation.
- Updated the release manifest to point at the new build.

## 1.0.1 - 2026-06-17

- Initial release with installer support and auto-update capability.
