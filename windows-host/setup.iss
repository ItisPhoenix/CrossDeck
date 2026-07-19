; Inno Setup Script for CrossDeck PC Host
; Compiles into a standard Windows Setup EXE installer.

[Setup]
AppName=CrossDeck
AppVersion=0.3.1-beta
AppPublisher=ItisPhoenix
AppPublisherURL=https://github.com/ItisPhoenix
DefaultDirName={userappdata}\CrossDeck
DefaultGroupName=CrossDeck
UninstallDisplayIcon={app}\CrossDeckHost.exe
Compression=lzma2
SolidCompression=yes
OutputDir=D:\CrossDeck\windows-host\Setup
OutputBaseFilename=CrossDeckSetup
SetupIconFile=D:\CrossDeck\windows-host\CrossDeckHost\Assets\app.ico
PrivilegesRequired=lowest

[Files]
Source: "D:\CrossDeck\windows-host\CrossDeckHost\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CrossDeck"; Filename: "{app}\CrossDeckHost.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\CrossDeck"; Filename: "{app}\CrossDeckHost.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\CrossDeckHost.exe"; Description: "Launch CrossDeck Host"; Flags: nowait postinstall skipifsilent
