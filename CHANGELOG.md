# Changelog

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
