param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have the form 1.0.0.' }
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet test DriveSpaceAnalyzer.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $publish = Join-Path $root 'artifacts/publish/win-x64'
    # Always clear this build output, never a scanned drive or user-selected path.
    if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    dotnet publish src/DriveSpaceAnalyzer.App -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    if (!(Test-Path $compiler)) { $compiler = (Get-Command ISCC.exe -ErrorAction Stop).Source }
    & $compiler "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$root/artifacts/installer" installer/DriveSpaceAnalyzer.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Compress-Archive -Path "$publish/*" -DestinationPath "artifacts/DriveSpaceAnalyzer-$Version-win-x64.zip" -Force
    Get-ChildItem artifacts/installer/*.exe, "artifacts/DriveSpaceAnalyzer-$Version-win-x64.zip" |
        Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
} finally { Pop-Location }
