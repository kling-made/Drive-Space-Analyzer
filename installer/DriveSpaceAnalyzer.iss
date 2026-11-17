#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{E2A0EC36-CC40-4A16-894E-80AAEF50501F}
AppName=Drive Space Analyzer
AppVersion={#AppVersion}
AppPublisher=kling-made
AppPublisherURL=https://github.com/kling-made/Drive-Space-Analyzer
AppSupportURL=https://github.com/kling-made/Drive-Space-Analyzer/issues
DefaultDirName={localappdata}\Programs\Drive Space Analyzer
DefaultGroupName=Drive Space Analyzer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=DriveSpaceAnalyzer-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DriveSpaceAnalyzer.exe
SetupLogging=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Drive Space Analyzer"; Filename: "{app}\DriveSpaceAnalyzer.exe"
Name: "{autodesktop}\Drive Space Analyzer"; Filename: "{app}\DriveSpaceAnalyzer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DriveSpaceAnalyzer.exe"; Description: "Launch Drive Space Analyzer"; Flags: nowait postinstall skipifsilent
