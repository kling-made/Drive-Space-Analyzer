# Drive Space Analyzer

A read-only, offline Windows 10/11 x64 desktop app for finding the folders and files that account for logical drive usage. Built with C#, .NET 10, WPF and MVVM.

**[Download the Windows installer](https://github.com/kling-made/Drive-Space-Analyzer/releases/latest)** · [Build and test status](https://github.com/kling-made/Drive-Space-Analyzer/actions/workflows/build.yml)

## Install and use

1. Download `DriveSpaceAnalyzer-Setup-1.0.0-win-x64.exe` from Releases and run it. Installation is per user and requires no administrator privileges. The .NET runtime is included; no internet connection is needed to run the app.
2. Select a logical drive. Its label, type, capacity, actual used space and available free space are displayed. Click **Refresh Drives** after attaching or removing a volume. A scan never starts automatically.
3. Set the minimum file size (default **1 GB**, exactly 1,000,000,000 bytes). Decimal MB/GB/TB and fractional values are supported. Only files **strictly greater** than this threshold appear in Large Files. The threshold used for each snapshot is shown above its file list.
4. Click **Scan Drive**. **Cancel Scan** works during discovery and analysis. Results from a canceled, failed or warned scan are visibly partial.
5. Sort either grid by clicking a column header. Sizes sort by the underlying 64-bit byte count. Search by name or full path without rescanning. Drag the divider or column boundaries to resize.
6. Double-click a folder row to open an independent window. Its left grid shows immediate subfolders with recursive sizes; its right grid shows qualifying files throughout that subtree. Continue double-clicking to navigate deeper. **Copy Path** copies that window's directory path.
7. Expand **Scan summary and skipped items** for totals, largest items, duration and error details. Only the first 200 diagnostic events are retained; the full skipped/error count is preserved.

A portable ZIP is also available. Extract **all** files to one folder and run `DriveSpaceAnalyzer.exe`. This is a self-contained **multi-file** application: supporting DLLs and runtime files must stay beside the executable. Uninstall an installed copy from Windows Settings → Apps.

The release is unsigned. Windows may display an unknown-publisher or SmartScreen prompt. No code-signing identity is included in this repository. Compare downloaded files with `SHA256SUMS.txt` using PowerShell `Get-FileHash -Algorithm SHA256`.

## What sizes mean

All sizes are **logical file lengths**, using decimal units (1 KB = 1,000 bytes). They are not uniquely allocated physical disk space. NTFS compression, sparse files, hard links and filesystem overhead can make logical totals differ from Windows' drive-used metric. Each hard-link directory entry is counted independently. Folder totals already include descendant files: do not add folder totals to one another or to the large-file list.

Hidden and system entries are included when accessible. Directory junctions, symbolic links, mount points and all other directory reparse points are skipped. File reparse points are also skipped, including cloud placeholders; the scanner never requests file hydration or reads file contents. Skipping any content produces warnings and incomplete indicators on affected ancestors. Denied, missing and changing entries are excluded from accounted byte totals. The scan is a best-effort metadata snapshot, not a transactional filesystem snapshot.

There are no delete, move, rename, edit, compression, formatting or cleanup operations in the application. It does not request elevation, use a network service, or write settings/logs into scanned directories. The installer writes only the application's installation files, shortcuts and uninstall registration.

## Build and test

On **Windows**, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio with .NET 10/WPF support can open `DriveSpaceAnalyzer.sln`; the CLI also works:

```powershell
dotnet restore DriveSpaceAnalyzer.sln
dotnet build DriveSpaceAnalyzer.sln -c Release
dotnet test DriveSpaceAnalyzer.sln -c Release
dotnet run --project src/DriveSpaceAnalyzer.App
```

The shipping app has no external package dependencies. Test projects use xUnit and Microsoft.NET.Test.Sdk. Core tests run on Linux/macOS too:

```sh
dotnet test tests/DriveSpaceAnalyzer.Tests -c Release
```

WPF tests require Windows and run real windows on an STA dispatcher. They exercise the Scan button, UI-thread notifications, numeric sorting, filtering, double-click navigation, nested windows, independent view state and closing behavior. Filesystem tests use controlled temporary trees or metadata fakes; they never scan an entire system volume. Tests only create/remove their own fixtures.

## Publish and build the installer

Install **Inno Setup 6.3 or later** on Windows (the script detects `Program Files (x86)\Inno Setup 6\ISCC.exe` or `ISCC.exe` on PATH), then run:

```powershell
./scripts/Publish.ps1 -Version 1.0.0
./scripts/SmokeTest-Installer.ps1 -Installer (Get-ChildItem artifacts/installer/*.exe | Select-Object -First 1).FullName
```

This runs both test projects, publishes a self-contained Windows x64 folder, builds a per-user installer and portable ZIP, and writes SHA-256 checksums. Outputs are in `artifacts/`. The smoke test installs to a unique temporary directory, launches the installed WPF app, checks a real main window and clean exit, then uninstalls.

To publish the app folder without creating an installer:

```powershell
dotnet publish src/DriveSpaceAnalyzer.App -c Release -p:PublishProfile=WindowsX64
```

The profile targets `win-x64`, includes the runtime, and disables trimming and single-file bundling. The executable is `DriveSpaceAnalyzer.exe`.

## Repository releases

Every push to `main` and pull request runs the Windows build, core/WPF tests, packaging and installer smoke test. To publish a release from an already reviewed commit:

```sh
git tag v1.0.0
git push origin v1.0.0
```

The tag workflow publishes the installer, portable ZIP and checksums **only after** Windows validation succeeds. GitHub provides source archives automatically. Future release tags must use `vMAJOR.MINOR.PATCH`; do not reuse published tags.

## Design and limits

See [Architecture and scanning behavior](docs/ARCHITECTURE.md) and [release notes](docs/RELEASE_NOTES.md).

Filesystem calls on disconnected or slow network drives can block inside Windows; cancellation takes effect when the current OS call returns. Reparse attributes are checked before each enumeration, but ordinary metadata APIs cannot make directory replacement races atomic. The two-phase scan can observe filesystem changes between discovery and analysis. New directories created after discovery may require another scan. Drive capacity/free values are captured from drive discovery and may change during scanning.

Memory scales with discovered folders plus files exceeding the threshold; nonqualifying individual file records are discarded immediately. A very low threshold on a volume with millions of files can consume significant RAM. Index construction, sorting and subtree preparation run off the UI thread, with virtualized grids and debounced filters. Sorting/filtering a very large visible result view can still briefly occupy the dispatcher. No artificial result cap hides scanned content.
