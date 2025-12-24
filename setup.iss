[Setup]
AppName=lf-windows
AppVersion=1.0
DefaultDirName={autopf}\lf-windows
DefaultGroupName=lf-windows
OutputBaseFilename=lf-windows-setup
Compression=lzma2
SolidCompression=yes
OutputDir=release
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Files]
Source: "release\lf-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\lf-windows"; Filename: "{app}\lf-windows.exe"
Name: "{autodesktop}\lf-windows"; Filename: "{app}\lf-windows.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\lf-windows.exe"; Description: "{cm:LaunchProgram,lf-windows}"; Flags: nowait postinstall skipifsilent
