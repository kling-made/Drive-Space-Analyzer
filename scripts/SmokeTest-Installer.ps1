param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:TEMP ('dsa-install-test-' + [guid]::NewGuid())
$installLog = Join-Path $env:TEMP 'dsa-install.log'
$process = Start-Process $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$installLog`" /DIR=`"$destination`"" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
try {
    $exe = Join-Path $destination 'DriveSpaceAnalyzer.exe'
    if (!(Test-Path $exe)) { throw 'Installed executable is missing.' }
    # Force the self-contained installation to work without the SDK's runtime.
    $originalRoot = $env:DOTNET_ROOT
    try {
        $env:DOTNET_ROOT = Join-Path $env:TEMP 'dsa-no-global-runtime'
        $app = Start-Process $exe -PassThru
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$app.HasExited -and $app.MainWindowHandle -eq 0 -and $timer.Elapsed.TotalSeconds -lt 30) {
            Start-Sleep -Milliseconds 250
            $app.Refresh()
        }
        if ($app.HasExited -or $app.MainWindowHandle -eq 0) { throw 'The installed app did not open its WPF window.' }
        if ($app.MainWindowTitle -ne 'Drive Space Analyzer') { throw 'Unexpected installed window title.' }
        $null = $app.CloseMainWindow()
        if (!$app.WaitForExit(10000)) { $app.Kill(); throw 'App did not close cleanly.' }
        if ($app.ExitCode -ne 0) { throw "App exited with $($app.ExitCode)." }
    } finally { $env:DOTNET_ROOT = $originalRoot }
} finally {
    $uninstaller = Join-Path $destination 'unins000.exe'
    if (Test-Path $uninstaller) {
        $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Uninstall failed.' }
    }
}
if (Test-Path (Join-Path $destination 'DriveSpaceAnalyzer.exe')) { throw 'Uninstall left the application installed.' }
Write-Host 'Installer, self-contained WPF startup, clean exit and uninstall passed.'
