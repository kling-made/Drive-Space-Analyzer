Drive Space Analyzer 1.0.0 is a read-only, offline Windows 10/11 x64 drive analysis app built with C#/.NET 10 and WPF.

- Drive selection with capacity, used and available-free space; no automatic scanning.
- Two-phase background scans with cancellation, elapsed time, measured directory progress and smoothed ETA.
- Recursive folder totals and strictly-greater-than large-file thresholds (default: 1 GB).
- Independent sortable/filterable grids and nested folder drill-down from the in-memory scan index.
- Access/reparse warnings, partial-result indicators and capped diagnostic details.
- Self-contained runtime, per-user installer and portable multi-file ZIP.

Download `DriveSpaceAnalyzer-Setup-1.0.0-win-x64.exe` to install, or extract the entire portable ZIP and launch `DriveSpaceAnalyzer.exe`. The source is available in this repository and GitHub's source archives. `SHA256SUMS.txt` covers both binary downloads.

The app and installer are unsigned; Windows may show an unknown-publisher/SmartScreen prompt. No administrator privileges or internet connection are needed to run the installed app. Logical file lengths can differ from physical disk allocation. Reparse points and inaccessible content are skipped and flagged.

The release workflow requires the Windows core tests, real WPF interaction test, self-contained publish, installer compilation, installed-app startup/exit and uninstall smoke test to pass before these assets are published. A whole-system C: scan is not an automated fixture; results on live drives depend on permissions and concurrent filesystem changes.
