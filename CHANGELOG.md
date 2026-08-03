# Changelog

## 1.2.0 - 2026-08-03

- Added behavior learning for launch flow with a recommended order model based on prior runs, including optional auto-apply and per-run override controls.
- Added a Program Builder tab so users can add or update launch steps directly in the app (program path, arguments, login flow, timing, and additional productivity notes) without hand-editing JSON.
- Added secure credential handling using an encrypted local secret vault (Windows DPAPI) and runtime token resolution (`{{secret:Name}}`) so login sequences can avoid storing plaintext secrets in config files.
- Added full in-app secret management (save, list, rename, delete, insert token) to support customer-friendly credential workflows for market release readiness.

## 1.1.40 - 2026-07-31

- Fixed the SQL Server login/password wait timing out early: the wait loop counted iterations as if each took a full second, but iterations that dismissed the Access confirmation dialog only slept 400-1000ms, so a run of dismiss iterations could burn through the whole configured timeout in far less real time than intended, reporting "not detected" right before the real login box actually appeared. The wait now tracks real elapsed time instead of iteration count, and no longer re-processes a confirmation dialog it already tried to dismiss on every poll.
- Self-update installs no longer run fully silent. The installer's own wizard now shows so it can ask to confirm, show real install progress, and offer to relaunch Launcher when finished, instead of the app just closing without any visible install feedback.

## 1.1.39 - 2026-07-31

- Fixed the update table flow still not waiting for the real SQL Server login box: Access shows an intermediate "You are about to update N record(s)" confirmation dialog first, which has no password field and needs a Yes/OK click to proceed. The flow now recognizes that a detected window without an edit field is this confirmation, automatically dismisses it, and keeps waiting for the actual login prompt (which does have a password field) before typing the password.
- Added a small delay between the simulated mouse-down and mouse-up in the button-click fallback for extra reliability.
- After downloading an update, the app now shows "Installing update. Launcher will reopen automatically when finished..." instead of just closing silently. The installer now auto-launches Launcher again after a silent self-update completes, so the app reappearing is a visible sign the update finished.

## 1.1.38 - 2026-07-31

- Fixed the update table flow occasionally grabbing a brief transient message box (e.g. an Access security/loading notice) that flashes before the real SQL Server login box appears, then typing the password after that box had already closed on its own.
- The password-window detection now re-checks a detected window after a short settle delay and ignores it if it already closed, continuing to wait for the real SQL Server login prompt instead.

## 1.1.37 - 2026-07-31

- Fixed the update table flow still sometimes entering the password before the SQL Server login box actually appeared. `"Microsoft Access"` was configured as a password-window title to match, but that title is also used by Access's own background frame window, which is already open before the button is even clicked - so it could be grabbed immediately instead of waiting for the real prompt.
- The password-window detection now requires every candidate match (not just the fallback path) to be a window that is genuinely new since the Update Table button was clicked, and `"Microsoft Access"` was moved out of the match list and into the exclude list.

## 1.1.36 - 2026-07-31

- Increased the update-table flow's post-password wait from 180 to 240 seconds, giving the Visual Board table update more time to finish before the launcher moves on to the next step.

## 1.1.35 - 2026-07-31

- Fixed a silent-install hang: the installer's "already installed, remove first?" and "launch now?" prompts used MessageBox, which NSIS does not auto-dismiss during silent (/S) installs. A silent install (including the app's own self-update flow) could sit forever waiting for a click that would never come.
- The installer now detects silent mode and skips those prompts entirely (auto-removing the previous version, and not auto-launching), so /S installs always complete on their own.

## 1.1.34 - 2026-07-31

- Help > About and Help > Check for Updates now open two completely separate windows instead of sharing one tabbed window. Clicking About only shows the about info; clicking Check for Updates only shows update checking/download.
- Reordered the Help menu so Check for Updates is listed first and About is last.

## 1.1.33 - 2026-07-31

- The Stop button was present and working (confirmed via UI Automation against the running app), but its disabled appearance relied on the OS default button theme, which washed its custom red coloring out to a barely-visible gray against the app's dark background.
- The Stop button now uses its own always-visible red styling (bright red "STOP" when a run is active, a clearly muted dark red when idle) instead of the default disabled theme, so it can no longer be mistaken for missing.

## 1.1.32 - 2026-07-31

- "Check for Updates" is now its own item in the Help menu (alongside About), instead of only being reachable as a tab inside the About window. Clicking it opens directly to the Updates tab and immediately starts checking.
- The Stop button (added in 1.1.30) remains available on the main window whenever a Start/Close run is in progress, so the automation can be halted immediately if something isn't right.

## 1.1.31 - 2026-07-31

- Added a dedicated "Updates" tab to the About window (About Launcher Native), separate from the general About info, with its own Check for Updates button and status area.
- The Download & Install flow now shows a real, in-app download progress bar (percentage and MB downloaded/total) instead of shelling out to an external PowerShell console window.
- Removed the "Updater started. Launcher will close now..." confirmation dialog. Once the downloaded installer is verified and launched, Launcher now closes immediately and automatically so the installer can replace the running files, without asking the user to click OK first.
- Update checking, downloading, and checksum verification now run in-process (new `LauncherUpdateService` in Launcher.Core) rather than relying on `Install-LauncherUpdate.ps1`.

## 1.1.30 - 2026-07-31

- Fixed the update table flow sending the password before the SQL Server login box actually appeared. Generic fallback titles like "Access"/"Microsoft Access" matched the main Access window instantly, so the flow assumed the password prompt was up and typed into whatever had focus.
- The update table flow now genuinely watches for the SQL Server login/password window: it first checks the specific configured password titles, then falls back to detecting any window that is genuinely new since the Update Table button was clicked, before typing the password.
- Added a Stop button to the Launcher UI so a running Start/Close automation can be halted immediately if something isn't right, instead of having to wait for it to finish or kill the app.
- Rebuilt the installer for these fixes.

## 1.1.29 - 2026-07-30

- Fixed the real reason login could silently fail to complete: the Enter-retry logic in the native login flow treated the main Access window being visible as proof of login success, but that window title is present the entire time Access is running, even while the login form is still open on top of it. This let the flow declare login "done" after the very first attempt, skip all retries, and skip the failure check entirely — leaving the app stuck on the login form while later steps (like update table) proceeded anyway.
- Login success is now judged solely by the login window itself closing, matching the legacy PowerShell contract, so Enter retries and the final failure check actually run when login hasn't completed.
- Rebuilt the installer for the login-success-detection fix.

## 1.1.28 - 2026-07-30

- Fixed the update table flow starting before Visual Board actually finished logging in. The main Access window title ("Access - Visual Board") appears as soon as the app launches, even while the login form is still processing, so the previous window-title-only check passed instantly and let the update table step race ahead.
- The update table flow now waits (up to a configurable `loginReadyTimeoutSeconds`, default 60s) for the real post-login control (the Update Table button, or an explicit `loginReadyControlNames` list) to actually appear in the window before clicking, matching the legacy PowerShell behavior.
- Rebuilt the installer for the login-readiness fix.

## 1.1.27 - 2026-07-30

- Root-caused the Visual Board login failure by comparing against the working 1.1.0 behavior: the native flow was auto-deriving a login value from the sequence and injecting it via UI Automation `ValuePattern.SetValue`, then skipping the real keystroke for that value. Access forms don't always register that synthetic write, leaving the field empty when Enter was sent.
- Direct UI-Automation value injection is now only used when a step explicitly configures `loginFieldValue` (matching the legacy PowerShell contract). Visual Board never configured this, so it now always types the full login sequence with real keystrokes, exactly like 1.1.0.
- Added a real mouse click on the login field's center before focusing it, matching the legacy click-then-focus behavior that reliably places the Access cursor.
- Rebuilt the installer for the login-behavior regression fix.

## 1.1.26 - 2026-07-30

- Fixed a Visual Board login regression by targeting the login field across both the login window and the app's main window before falling back to keyboard input.
- Login field focus is now reused for the keyboard fallback path so the value lands in the correct control.
- Added a regression test project covering the login-value extraction logic.
- Rebuilt the installer for the login-targeting regression fix.

## 1.1.24 - 2026-07-30

- Restored direct UI Automation entry into the Visual Board employee-ID field in the native launcher, matching the earlier PowerShell flow more closely.
- Kept the login-sequence submission and update-table automation in the native start flow intact after login completion.
- Relaxed native Visual Board login completion handling so the launcher no longer aborts when the main window is already ready even if the login form remains visible.
- Rebuilt the installer for the native Visual Board login verification hotfix.

## 1.1.23 - 2026-07-30

- Targeted the native Visual Board employee-ID login field directly before sending the configured key sequence.
- Added fallback clear/type/retry handling so login retries work more reliably when the field is not immediately ready.
- Rebuilt the installer for the native Visual Board login reliability release line.

## 1.1.22 - 2026-07-29

- Reverted native login input priming for Visual Board and restored the prior key-sequence login behavior.
- Keeps strict/fallback login window activation improvements while removing the priming path that changed login behavior.

## 1.1.21 - 2026-07-29

- Fixed native Access running detection to ignore generic window-title matches ("Access", "Microsoft Access") for Access-hosted steps.
- Prevents Stockroom Analytics from being skipped when Visual Board (or another Access app) is open.

## 1.1.20 - 2026-07-29

- Fixed native Access running-detection so Stockroom Analytics is no longer skipped just because another Access session is open.
- Improved update-table button activation in native start flow with retry timing, wider window-scope fallback, and parent-control invoke support.
- Added optional `updateTableButtonTimeoutSeconds` in `updateTableFlow` for slow UI readiness cases.

## 1.1.19 - 2026-07-29

- Hardened native Visual Board login priming so login input write failures no longer abort startup flow with "Operation cannot be performed".
- Added safe fallback behavior when direct UI Automation input setting fails, allowing sequence typing to continue.
- Added a branded native application icon used by the app window and desktop shortcut target.

## 1.1.18 - 2026-07-29

- Improved native Visual Board login targeting by prioritizing the login window and priming the login input field before key-sequence submission.
- Added fallback activation behavior so startup can continue when strict login-window matching is unavailable.
- Rebuilt the installer for the native login-targeting reliability release line.

## 1.1.17 - 2026-07-29

- Relaxed Visual Board login verification so startup no longer fails when the login form remains visible but the main window and controls are ready.
- Kept native start flow behavior and Visual Board update-table automation unchanged.
- Rebuilt the installer for the login-verification reliability release line.

## 1.1.16 - 2026-07-29

- Forced the native Launcher window to maximize on load so it opens full screen consistently.
- Kept the native start flow and Visual Board automation intact.
- Rebuilt the installer for the startup-maximized native launcher release line.

## 1.1.15 - 2026-07-29

- Set the native Launcher window to open maximized by default.
- Kept the native start flow and Visual Board automation changes in place.
- Rebuilt the installer for the maximized native launcher release line.

## 1.1.14 - 2026-07-29

- Moved the main Start flow into the native app so launch execution no longer depends on the PowerShell bridge.
- Preserved Visual Board login and update-table automation in the native start runner, including the password step and post-update wait.
- Aligned the launch order so update-table runs only after login completion is confirmed, then rebuilt the installer for the native-start release line.

## 1.1.13 - 2026-07-22

- Fixed native update behavior so choosing an available update now launches the installer flow.
- Native updater now reads installer URL/checksum from both versions.json and GitHub latest-release payloads.
- Launcher closes after starting the updater so installation can proceed.

## 1.1.12 - 2026-07-22

- Published a new tagged release so the update checker can be validated against a newer GitHub release.
- No code changes beyond the release metadata bump.
- Rebuilt the installer for the update-check test target.

## 1.1.11 - 2026-07-22

- Fixed the native About dialog update check so it resolves GitHub releases instead of stopping at the raw manifest URL.
- Added support for both the existing versions.json feed and GitHub's latest-release payload.
- Rebuilt the installer for the updated update-check behavior.

## 1.1.10 - 2026-07-22

- Fixed Access-backed launch detection so Stockroom Analytics no longer inherits another app's MSACCESS state.
- Removed the generic Access process fallback for launch checks so each database is evaluated on its own path and titles.
- Rebuilt the installer for the updated launch detection behavior.

## 1.1.9 - 2026-07-22

- Resolved PowerShell analyzer warnings in the close/session helpers.
- Renamed close helper functions to approved/singular naming and updated all call sites.
- Kept runtime behavior the same while improving script health and diagnostics.

## 1.1.8 - 2026-07-22

- Fixed desktop shortcut refresh so `Launcher.lnk` now stays pointed to the native `Launcher.exe` when available.
- Prevented launcher script runs from overriding the native app as the default entrypoint.
- Improved native menu readability with stronger contrast, larger text, and clearer highlight states.

## 1.1.7 - 2026-07-22

- Added a native top menu bar with `File > Exit` and `Help > About`.
- Added an About dialog with app description, creator credit, and copyright details.
- Added `Check for Updates` action inside About that checks the configured update feed and compares versions.

## 1.1.6 - 2026-07-22

- Native WPF app now starts with Dry Run disabled by default.
- Normal Start/Close actions run live unless Dry Run is explicitly enabled in the UI.
- Rebuilt the installer for the updated native default behavior.

## 1.1.5 - 2026-07-22

- Switched the installed launcher shortcut to the native WPF app instead of the PowerShell wrapper.
- The installer now publishes `Launcher.App.exe` from `native\publish\win-x64` and installs it as `Launcher.exe`.
- Rebuilt the installer for the native app release line.

## 1.1.4 - 2026-07-22

- Added Visual Mfg login titles to the running-state candidates so an already-open login window is recognized as already running.
- Rebuilt the installer for the updated release line.

## 1.1.3 - 2026-07-22

- Fixed launch-step reruns so configured title and process signals are treated as alternatives rather than required together.
- Applied the same running-state rule in the native detection service.
- Rebuilt the installer for the updated release line.

## 1.1.2 - 2026-07-22

- Fixed Access-backed rerun detection so Visual Board and Stockroom Analytics recognize already-open databases even when the command line path form changes.
- Broadened the Access running check to match both the normalized database path and the database filename.
- Rebuilt the installer for the updated release line.

## 1.1.1 - 2026-07-22

- Fixed rerun detection so Access-backed launch steps now recognize already-open Visual Board and Stockroom Analytics sessions.
- Added MSACCESS to the running-state process checks for `.accdb` and `.accde` targets.
- Rebuilt the installer for the updated release line.

## 1.1.0 - 2026-07-17

- Promoted the native migration baseline to the active version line to avoid false update prompts from older 1.0.x history.
- Preserved the native desktop migration scope: WPF app shell (`Launcher.App`) plus shared core library (`Launcher.Core`).
- Preserved native settings edit/save flow and initial native running-state and close-target detection actions.
- Preserved native win-x64 publish profile and publish script workflow.

## 1.0.0 - 2026-07-17 (Build 1)

- Introduced the first native desktop migration baseline with a WPF application shell and shared core library.
- Added native settings editing and save flow for launcher defaults and step enablement.
- Added native detect actions for running-state and close-target discovery, while preserving script-bridge execution modes.
- Added native publish profile and publishing script for win-x64 distribution.

## 1.0.37 - 2026-07-17

- Resolved duplicate analyzer warnings for lock-key helper verbs in `launcher.ps1`.
- Updated lock-key helper structure to satisfy `ShouldProcess` expectations.
- Rebuilt installer and refreshed release metadata for v1.0.37.

## 1.0.36 - 2026-07-17

- Fixed Stockroom Analytics launch to start Access databases via `MSACCESS.EXE /nostartup`.
- Prevented extra Access start-page windows during the Stockroom Analytics step.
- Rebuilt installer and refreshed release metadata for v1.0.36.

## 1.0.35 - 2026-07-17

- Updated Receiver month folder naming to `MMMM yyyy` (example: `July 2026`).
- Kept Receiver date folder naming as `MM_dd_yyyy` (example: `07_17_2026`).
- Rebuilt installer and refreshed release metadata for v1.0.35.

## 1.0.34 - 2026-07-16

- Added Receiver's step folder flow: check/create current month folder, then check/create current date folder.
- Date folder format is now configurable and set to `MM_dd_yyyy` for Receiver's (example: `07_16_2026`).
- Rebuilt installer and refreshed release metadata for v1.0.34.

## 1.0.33 - 2026-07-16

- Added explicit per-step running-check logs so each step reports running/not-running before launch.
- Tightened step-specific running window aliases to reduce false positives from generic titles.
- Rebuilt installer and refreshed release metadata for v1.0.33.

## 1.0.32 - 2026-07-16

- Tightened step running-state detection to reduce false skips when apps are closed.
- Running checks now combine window and process signals when both are configured, and avoid inferred process checks for folder targets.
- Rebuilt installer and refreshed release metadata for v1.0.32.

## 1.0.31 - 2026-07-16

- Fixed Visual Mfg false "already running" detection by removing overly broad window/process match terms.
- Visual Mfg now skips only on specific Visual Manufacturing indicators.
- Rebuilt installer and refreshed release metadata for v1.0.31.

## 1.0.30 - 2026-07-16

- Published a maintenance release to keep the installer, manifest, and runtime versioning aligned.
- Rebuilt the installer package for distribution.

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
