; Compile with scripts/build-installer.ps1. Stable AppId supports in-place upgrades.
#ifndef AppVersion
  #error AppVersion must be supplied by the build script
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by the build script
#endif
#ifndef OutputDirPath
  #error OutputDirPath must be supplied by the build script
#endif

[Setup]
AppId={{194394EC-A8BE-4DAB-8E69-B1B73DD96B1F}
AppName=Pi Harbor
AppVersion={#AppVersion}
AppPublisher=BlazarLin
AppPublisherURL=https://github.com/BlazarLin/Pi-Harbor
AppSupportURL=https://github.com/BlazarLin/Pi-Harbor/issues
AppUpdatesURL=https://github.com/BlazarLin/Pi-Harbor/releases
DefaultDirName={localappdata}\Programs\Pi Harbor
DefaultGroupName=Pi Harbor
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDirPath}
OutputBaseFilename=Pi-Harbor-Setup-win-x64
SetupIconFile=..\src\PIHarness.App\Assets\Pi-Harbor.ico
UninstallDisplayIcon={app}\Pi-Harbor.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Pi Harbor"; Filename: "{app}\Pi-Harbor.exe"
Name: "{autodesktop}\Pi Harbor"; Filename: "{app}\Pi-Harbor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Pi-Harbor.exe"; Description: "Launch Pi Harbor"; Flags: nowait postinstall skipifsilent

; No user-session, project, credential, or settings directories are removed on uninstall.
